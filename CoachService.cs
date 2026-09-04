using System.Net.Http.Json;
using System.Text.Json;

namespace DiabloEnglishCoach;

internal sealed class CoachService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };

    public async Task<CoachReply> ExplainAsync(string original, CoachConfig config, CancellationToken cancellationToken)
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
You are a spoiler-safe English coach shown over Diablo Immortal.
You may use ONLY the current OCR sentence supplied by the user. Never use franchise knowledge,
character biographies, wikis, later quests, or predictions. Never reveal future identities,
motives, bosses, locations, rewards, or plot events. If the sentence does not establish a fact,
say in Traditional Chinese: 目前這句還沒有說明.

Return one JSON object only:
{
  "simple_english": "A short A2-B1 paraphrase, without adding facts",
  "traditional_chinese": "Natural Traditional Chinese (Taiwan), without adding facts",
  "keywords": [{"word":"word or short phrase from the sentence","meaning":"short Traditional Chinese meaning"}]
}
Choose zero to three useful keywords that appear in the sentence. Do not continue the story.
"""
                    },
                    new { role = "user", content = $"Current on-screen sentence only:\n{original}" }
                },
                options = new
                {
                    temperature = 0.1,
                    num_ctx = 2048,
                    num_predict = 160,
                    // Keep the MX330's 2 GB VRAM free for Diablo Immortal. The
                    // 2B coach model fits comfortably in system RAM on this PC.
                    num_gpu = config.ForceCpuInference ? 0 : -1
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var envelope = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var content = envelope.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            return ParseModelReply(original, content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return MakeFallback(original, FriendlyOllamaError(exception));
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

    internal static CoachReply ParseModelReply(string original, string content)
    {
        var firstBrace = content.IndexOf('{');
        var lastBrace = content.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            content = content[firstBrace..(lastBrace + 1)];

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var simple = ReadString(root, "simple_english");
        var chinese = ReadString(root, "traditional_chinese");
        var keywords = new List<KeywordCard>();

        if (root.TryGetProperty("keywords", out var keywordElement) && keywordElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in keywordElement.EnumerateArray().Take(3))
            {
                var word = ReadString(item, "word");
                var meaning = ReadString(item, "meaning");
                if (word.Length > 0 && meaning.Length > 0 && original.Contains(word, StringComparison.OrdinalIgnoreCase))
                    keywords.Add(new KeywordCard(word, meaning));
            }
        }

        return new CoachReply(
            original,
            simple.Length > 0 ? simple : original,
            chinese.Length > 0 ? chinese : "目前這句還沒有說明。",
            keywords,
            true);
    }

    private static CoachReply MakeFallback(string original, string notice)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["follow"] = "跟隨", ["find"] = "尋找", ["talk to"] = "與……交談", ["defeat"] = "擊敗",
            ["kill"] = "擊殺", ["enter"] = "進入", ["leave"] = "離開", ["return"] = "返回",
            ["rescue"] = "營救", ["collect"] = "收集", ["equip"] = "裝備", ["upgrade"] = "升級",
            ["quest"] = "任務", ["reward"] = "獎勵", ["skill"] = "技能", ["damage"] = "傷害",
            ["health"] = "生命值", ["enemy"] = "敵人", ["undead"] = "不死族", ["shard"] = "碎片",
            ["cemetery"] = "墓園", ["sanctuary"] = "聖休亞瑞／庇護之地", ["waypoint"] = "傳送點",
            ["chest"] = "寶箱", ["inventory"] = "背包", ["legendary"] = "傳奇的", ["nearby"] = "附近"
        };

        var keywords = dictionary
            .Where(pair => original.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Key.Length)
            .Take(3)
            .Select(pair => new KeywordCard(pair.Key, pair.Value))
            .ToArray();

        return new CoachReply(original, original, "（啟動本機模型後會在這裡顯示繁中翻譯。）", keywords, false, notice);
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
