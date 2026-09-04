using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed class CoachService
{
    public BuildGuideCache? BuildGuides { get; set; }
    private readonly CoachKnowledgeCache _knowledge = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    public async Task<CoachReply> ExplainAsync(string original, CoachConfig config, CancellationToken cancellationToken)
    {
        // Proper names are unnecessary for teaching a quest phrase and tempt tiny
        // models into franchise recall. Keep them out of this inference task.
        var phrase = LearningPhrase(original);
        var teachingContext = phrase ?? original;
        if (_knowledge.TryGet("english", teachingContext, original, out var cached))
            return cached;
        var reply = await RequestAsync(
            original,
            teachingContext,
            "Teach the English phrase only. Give one hypothetical everyday English example (not a game instruction) and explain usage in Traditional Chinese. Do not describe a character or enemy. Return at most one keyword. Chinese under 30 characters, English under 8 words.",
            config,
            cancellationToken);
        reply = reply with { TraditionalChinese = "英文練習（不是新任務）：" + reply.TraditionalChinese };
        _knowledge.Store("english", teachingContext, reply);
        return reply;
    }

    internal static string? LearningPhrase(string source) =>
        new[] { "search for", "head to", "head forward", "talk to", "stay close", "return to", "follow", "defeat", "find", "enter", "leave" }
            .FirstOrDefault(phrase => Regex.IsMatch(source, @"\b" + Regex.Escape(phrase) + @"\b", RegexOptions.IgnoreCase));

    public async Task<CoachReply> GuideQuestAsync(
        string quest,
        string dialogue,
        CoachConfig config,
        CancellationToken cancellationToken,
        bool includeBuildTip = false)
    {
        var visibleContext = $"VISIBLE QUEST:\n{quest}\n\nVISIBLE DIALOGUE:\n{dialogue}";
        var cacheContext = $"{quest}\n{dialogue}";
        if (_knowledge.TryGet("quest", cacheContext, $"QUEST · {quest}", out var cached))
            return includeBuildTip ? BuildAdvisor.AppendTip(cached, config, quest, BuildGuides) : cached;
        var reply = await RequestAsync(
            $"QUEST · {quest}",
            visibleContext,
            "Act as a friendly game-and-English coach. Restate ONLY the action explicitly written in the visible quest; do not add a second action. Start the Traditional Chinese field with 現在要做：, then add one short 英文小知識： about a quest verb or phrase. If useful, you may say that selecting the visible quest entry may show a marker, but label this as 操作建議. Never suggest talking, fighting, collecting, or interacting unless that exact action appears in the visible text. Do not invent a route, target, NPC, reward, or later event.",
            config,
            cancellationToken);
        var grounded = GroundCommonQuestInstruction(quest, reply);
        _knowledge.Store("quest", cacheContext, grounded);
        return includeBuildTip ? BuildAdvisor.AppendTip(grounded, config, quest, BuildGuides) : grounded;
    }

    private async Task<CoachReply> RequestAsync(
        string displayOriginal,
        string allowedSource,
        string task,
        CoachConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = new Uri(new Uri(config.OllamaUrl.TrimEnd('/') + "/"), "api/chat");
            var request = new
            {
                model = config.Model,
                stream = false,
                think = false,
                format = "json",
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = """
You are a friendly, spoiler-safe English tutor. The task may also request a literal reading of an on-screen instruction.
You may use ONLY the visible OCR text supplied by the user. You may explain
general English, but never use franchise knowledge, character biographies, wikis, walkthroughs,
later quests, or predictions. Never reveal future identities, motives, bosses, locations, rewards,
or plot events. If the visible text does not establish a gameplay fact, say: 目前畫面沒有說明.
Keep the player engaged, warm, and concise. Do not lecture.

Return one JSON object only:
{
  "simple_english": "At most 8 English words",
  "traditional_chinese": "At most 40 Traditional Chinese characters, one useful tip",
  "keywords": [{"word":"word or short phrase visible in the supplied text","meaning":"short Traditional Chinese meaning plus usage when useful"}]
}
Choose zero or ONE useful keyword that actually appears in the supplied visible text. Keep its meaning under 8 Chinese characters. Do not continue the story.
"""
                    },
                    new { role = "user", content = $"TASK:\n{task}\n\nCURRENT VISIBLE CONTEXT:\n{allowedSource}" }
                },
                options = new
                {
                    temperature = 0.1,
                    num_ctx = 2048,
                    num_predict = 110,
                    num_thread = Math.Clamp(config.InferenceThreads, 1, 4),
                    // Keep the MX330's 2 GB VRAM free for Diablo Immortal. The
                    // 2B coach model fits comfortably in system RAM on this PC.
                    num_gpu = config.ForceCpuInference ? 0 : -1
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var envelope = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var content = envelope.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            return ParseModelReply(displayOriginal, allowedSource, content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return MakeFallback(displayOriginal, allowedSource, FriendlyOllamaError(exception));
        }
    }

    public async Task<(bool Running, bool ModelReady, string Message)> CheckAsync(CoachConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var tagsEndpoint = new Uri(new Uri(config.OllamaUrl.TrimEnd('/') + "/"), "api/tags");
            using var response = await _httpClient.GetAsync(tagsEndpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var models = document.RootElement.GetProperty("models").EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            var ready = models.Any(name => ModelNamesMatch(name!, config.Model));
            return ready
                ? (true, true, $"本機模型已就緒：{config.Model}")
                : (true, false, $"Ollama 已啟動，但尚未下載 {config.Model}");
        }
        catch
        {
            return (false, false, "尚未偵測到 Ollama；字幕 OCR 仍可使用，但繁中翻譯會顯示基本詞彙提示。");
        }
    }

    internal static CoachReply ParseModelReply(string original, string content) => ParseModelReply(original, original, content);

    private static CoachReply ParseModelReply(string displayOriginal, string allowedSource, string content)
    {
        var firstBrace = content.IndexOf('{');
        var lastBrace = content.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            content = content[firstBrace..(lastBrace + 1)];

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var simple = ReadString(root, "simple_english");
        var chinese = ReadString(root, "traditional_chinese");
        foreach (var pair in new[] { ("boss", "頭領|首領|頭目"), ("enemy", "敵人|敵方"), ("kill", "擊殺|殺死") })
        {
            if (!Regex.IsMatch(allowedSource, @"\b" + pair.Item1 + @"\b", RegexOptions.IgnoreCase) &&
                (Regex.IsMatch(simple, @"\b" + pair.Item1 + @"\b", RegexOptions.IgnoreCase) || Regex.IsMatch(chinese, pair.Item2)))
                return MakeFallback(displayOriginal, allowedSource, "模型追加了未提供的敵我描述，已改用本機英文提示。");
        }
        var keywords = new List<KeywordCard>();

        if (root.TryGetProperty("keywords", out var keywordElement) && keywordElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in keywordElement.EnumerateArray().Take(3))
            {
                var word = ReadString(item, "word");
                var meaning = ReadString(item, "meaning");
                if (word.Length > 0 && meaning.Length > 0 && allowedSource.Contains(word, StringComparison.OrdinalIgnoreCase))
                    keywords.Add(new KeywordCard(word, meaning));
            }
        }

        return new CoachReply(
            displayOriginal,
            simple.Length > 0 ? simple : displayOriginal,
            chinese.Length > 0 ? chinese : "目前這句還沒有說明。",
            keywords,
            true);
    }

    private static CoachReply MakeFallback(string displayOriginal, string allowedSource, string notice)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["search for"] = "尋找", ["head forward"] = "往前走", ["head to"] = "前往",
            ["follow"] = "跟隨", ["find"] = "尋找", ["talk to"] = "與……交談", ["defeat"] = "擊敗",
            ["kill"] = "擊殺", ["enter"] = "進入", ["leave"] = "離開", ["return"] = "返回",
            ["rescue"] = "營救", ["collect"] = "收集", ["equip"] = "裝備", ["upgrade"] = "升級",
            ["quest"] = "任務", ["reward"] = "獎勵", ["skill"] = "技能", ["damage"] = "傷害",
            ["health"] = "生命值", ["enemy"] = "敵人", ["undead"] = "不死族", ["shard"] = "碎片",
            ["cemetery"] = "墓園", ["sanctuary"] = "聖休亞瑞／庇護之地", ["waypoint"] = "傳送點",
            ["chest"] = "寶箱", ["inventory"] = "背包", ["legendary"] = "傳奇的", ["nearby"] = "附近"
        };

        var keywords = dictionary
            .Where(pair => allowedSource.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Key.Length)
            .Take(3)
            .Select(pair => new KeywordCard(pair.Key, pair.Value))
            .ToArray();

        var teaching = keywords.Length > 0
            ? $"英文小補充：{keywords[0].Word} 是「{keywords[0].Meaning}」。先抓住這個字，就比較容易理解畫面意思。"
            : "這句還沒有可靠的補充說明，先以畫面的原文和翻譯為準。";
        return new CoachReply(displayOriginal, displayOriginal, teaching, keywords, false, notice);
    }

    private static CoachReply GroundCommonQuestInstruction(string quest, CoachReply reply)
    {
        // Tiny local models can occasionally append a plausible-sounding next
        // action. Ground the most common quest imperative deterministically so
        // the overlay never turns "Head to X" into "go to X and talk to Y".
        var patterns = new (string Pattern, string EnglishVerb, string ChineseTemplate, string Keyword, string Knowledge)[]
        {
            (@"\bhead\s*to\s*(.+?)(?:[.!?]|$)", "Go to", "前往 {0}", "head to", "head to 表示「前往某地」，常見於任務與方向指示。"),
            (@"\btalk\s*to\s*(.+?)(?:[.!?]|$)", "Talk to", "與 {0} 交談", "talk to", "talk to 表示「與某人交談」。"),
            (@"\bdefeat\s+(.+?)(?:[.!?]|$)", "Defeat", "擊敗 {0}", "defeat", "defeat 是「擊敗」，後面直接接敵人或對手。"),
            (@"\bfollow\s+(.+?)(?:[.!?]|$)", "Follow", "跟隨 {0}", "follow", "follow 是「跟隨」，任務中常用來指示移動。"),
            (@"\bfind\s+(.+?)(?:[.!?]|$)", "Find", "尋找 {0}", "find", "find 是「找到／尋找」，強調找到結果。"),
            (@"\benter\s+(.+?)(?:[.!?]|$)", "Enter", "進入 {0}", "enter", "enter 作動詞時可直接接地點，不必加 into。")
        };

        foreach (var item in patterns)
        {
            var match = Regex.Match(quest, item.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            var target = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
            if (target.Length == 0)
                continue;

            var keywords = new List<KeywordCard>
            {
                new(item.Keyword, item.Knowledge)
            };
            keywords.AddRange(reply.Keywords.Where(existing =>
                !keywords.Any(added => added.Word.Equals(existing.Word, StringComparison.OrdinalIgnoreCase))));

            return reply with
            {
                SimpleEnglish = $"{item.EnglishVerb} {target}.",
                TraditionalChinese = $"現在要做：{string.Format(item.ChineseTemplate, target)}。 英文小知識：{item.Knowledge}",
                Keywords = keywords.Where(keyword => keyword.Word.Length > 0).Take(3).ToArray()
            };
        }

        var chinese = reply.TraditionalChinese.StartsWith("現在要做：", StringComparison.Ordinal)
            ? reply.TraditionalChinese
            : $"現在要做：{reply.TraditionalChinese}";
        return reply with { TraditionalChinese = chinese };
    }

    private static string FriendlyOllamaError(Exception exception) => exception switch
    {
        HttpRequestException => "無法連到 Ollama。請執行「安裝本機模型.cmd」，或確認 Ollama 正在執行。",
        TaskCanceledException => "本機模型超過 45 秒仍未回覆；本次只顯示 OCR 與基本詞彙。",
        JsonException => "模型回覆格式不完整；本次只顯示 OCR 與基本詞彙。",
        _ => $"本機模型暫時無法使用：{exception.Message}"
    };

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static bool ModelNamesMatch(string installedName, string requestedName)
    {
        static string Normalize(string name) => name.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
            ? name[..^7]
            : name;
        return string.Equals(Normalize(installedName), Normalize(requestedName), StringComparison.OrdinalIgnoreCase);
    }
}
