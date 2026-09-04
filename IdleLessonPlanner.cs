using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record IdleLesson(string Title, string English, string Chinese, string Topic = "game", string SentenceMeaning = "");

internal sealed record LessonPersonalizationContext(
    string Topic,
    string RecentLessons,
    string EncounteredWords);

// The local curriculum keeps narration available while the model prepares a
// personalized item. Generated lessons are cached locally and exact/nearby
// repetitions are rejected before they can be spoken.
internal sealed class IdleLessonPlanner
{
    public BuildGuideCache? Guides { get; set; }
    internal const long IntervalMs = 1_000;
    internal const long QuietWindowMs = 10_000;
    private const int GeneratedQueueTarget = 3;
    private const int RecentLimit = 12;
    private const int GeneratedArchiveLimit = 48;
    private readonly string? _cachePath;
    private readonly Queue<IdleLesson> _generated = new();
    private readonly Queue<IdleLesson> _recent = new();
    private readonly List<IdleLesson> _generatedArchive = new();
    private readonly HashSet<string> _knownGenerated = new(StringComparer.Ordinal);
    private readonly List<KeywordCard> _words = new();
    private long _nextDue;
    private long? _quietSince;
    private int _lessonIndex;
    private int _wordIndex;
    private int _tipIndex;
    private int _toeicIndex;
    private int _icIndex;
    private int _gameIndex;
    private int _topicIndex;

    private static readonly IdleLesson[] ToeicLessons =
    [
        new("多益核心字", "Please confirm the delivery schedule.", "confirm＝確認；schedule＝時程。商務郵件常用 confirm 來核對時間。", "toeic"),
        new("多益核心字", "The meeting has been postponed.", "postpone＝延期。has been postponed 表示會議已被延期。", "toeic"),
        new("多益核心字", "Employees must comply with the policy.", "comply with＝遵守。後面常接 policy、rule 或 regulation。", "toeic"),
        new("多益核心字", "The position requires relevant experience.", "require＝需要；relevant＝相關的。職缺說明裡很常見。", "toeic"),
        new("多益核心字", "We appreciate your prompt response.", "appreciate＝感謝；prompt＝迅速的。這是正式郵件常見用法。", "toeic"),
        new("多益核心字", "The device is temporarily unavailable.", "temporarily＝暫時地；unavailable＝無法使用的。", "toeic"),
        new("多益核心字", "Sales increased significantly this quarter.", "significantly＝顯著地；quarter＝季度。留意副詞修飾 increased。", "toeic"),
        new("多益核心字", "Submit the form before the deadline.", "submit＝提交；deadline＝截止期限。before 後面接時間點。", "toeic")
    ];

    private static readonly IdleLesson[] DigitalIcLessons =
    [
        new("數位 IC 面試字", "Explain the difference between latency and throughput.", "latency＝延遲；throughput＝吞吐量。面試時先分別定義，再舉例比較。", "ic"),
        new("數位 IC 面試字", "A flip-flop stores one bit of state.", "flip-flop＝正反器；state＝狀態。描述時可用 store one bit。", "ic"),
        new("數位 IC 面試字", "Setup time is checked before the clock edge.", "setup time＝建立時間；clock edge＝時脈邊緣。before 是關鍵方向字。", "ic"),
        new("數位 IC 面試字", "Hold time is checked after the clock edge.", "hold time＝保持時間。可和 setup time 的 before／after 對照記憶。", "ic"),
        new("數位 IC 面試字", "Use nonblocking assignments in sequential logic.", "nonblocking assignment＝非阻塞賦值；sequential logic＝循序邏輯。", "ic"),
        new("數位 IC 面試字", "Combinational logic should avoid unintended latches.", "combinational＝組合的；unintended latch＝非預期鎖存器。", "ic"),
        new("數位 IC 面試字", "A synchronizer reduces metastability risk.", "synchronizer＝同步器；metastability＝亞穩態。回答時說 reduces risk，不要說完全消除。", "ic"),
        new("數位 IC 面試字", "Pipelining can improve the maximum clock frequency.", "pipelining＝管線化；clock frequency＝時脈頻率。也要能說明 latency 的取捨。", "ic")
    ];

    private static readonly IdleLesson[] GameLessons =
    [
        new("遊戲英文", "Head to the marked area.", "head to＝前往；marked area＝標記區域。先抓任務句中的動詞。", "game"),
        new("遊戲英文", "Wait for the cooldown.", "cooldown＝冷卻時間。技能未恢復時先走位。", "game"),
        new("遊戲英文", "This effect increases skill damage.", "effect＝效果；increase＝增加。確認它是否強化你的核心技能。", "game"),
        new("遊戲英文", "Keep a defensive option.", "defensive＝防禦性的；option＝選項。配置中可保留移動或保命技能。", "game"),
        new("遊戲英文", "Pick up the loot.", "pick up＝撿起；loot＝戰利品。", "game"),
        new("遊戲英文", "The skill deals area damage.", "deal damage＝造成傷害；area damage＝範圍傷害。", "game"),
        new("遊戲英文", "Compare one item at a time.", "compare＝比較；at a time＝每次。一次換一件才容易判斷效果。", "game"),
        new("遊戲英文", "Interact with the object.", "interact with＝與……互動。通常要靠近目標再按互動鍵。", "game")
    ];

    public IdleLessonPlanner(string? cachePath = null, bool persistGenerated = true)
    {
        _cachePath = persistGenerated
            ? cachePath ?? Path.Combine(CoachConfig.FolderPath, "personalized-lessons-v2.json")
            : null;
        LoadGenerated();
    }

    public bool NeedsPersonalizedLesson => _generated.Count < GeneratedQueueTarget;

    public void Reset(long now)
    {
        _nextDue = now + IntervalMs;
        _quietSince = null;
    }

    public void ObserveLesson(long now, IReadOnlyList<KeywordCard> words, bool resetSchedule = true)
    {
        if (resetSchedule) Reset(now);
        foreach (var word in words)
        {
            if (word.Word.Length > 32 || word.Meaning.Length > 100 ||
                _words.Any(existing => existing.Word.Equals(word.Word, StringComparison.OrdinalIgnoreCase)))
                continue;
            _words.Add(word);
            if (_words.Count > 24)
                _words.RemoveAt(0);
        }
    }

    public LessonPersonalizationContext CreatePersonalizationContext(CoachConfig config)
    {
        var patterns = config.LearningFocus switch
        {
            LearningFocusOptions.ToeicFirst => new[] { "toeic", "toeic", "ic", "game" },
            LearningFocusOptions.DigitalIcFirst => new[] { "ic", "ic", "toeic", "game" },
            _ => new[] { "toeic", "ic", "game" }
        };
        var topic = patterns[_topicIndex++ % patterns.Length];
        var recent = _recent.Count == 0
            ? "none"
            : string.Join(" | ", _recent.TakeLast(8).Select(item => $"{item.Title}: {item.English}"));
        var words = _words.Count == 0
            ? "none yet"
            : string.Join(", ", _words.TakeLast(10).Select(item => $"{item.Word}={item.Meaning}"));
        return new LessonPersonalizationContext(topic, recent, words);
    }

    public bool AddPersonalizedLesson(IdleLesson lesson)
    {
        var signature = Signature(lesson.English);
        if (!Valid(lesson) || _knownGenerated.Contains(signature) ||
            _recent.Any(item => TooSimilar(item.English, lesson.English)) ||
            _generated.Any(item => TooSimilar(item.English, lesson.English)))
            return false;

        _generated.Enqueue(lesson);
        _knownGenerated.Add(signature);
        _generatedArchive.Add(lesson);
        while (_generatedArchive.Count > GeneratedArchiveLimit)
        {
            _knownGenerated.Remove(Signature(_generatedArchive[0].English));
            _generatedArchive.RemoveAt(0);
        }
        SaveGenerated();
        return true;
    }

    public IdleLesson? TryNext(long now, bool safeToTeach, CoachConfig config, bool requireQuietWindow = true)
    {
        if (!safeToTeach)
        {
            _quietSince = null;
            return null;
        }
        _quietSince ??= now;
        if (now < _nextDue || (requireQuietWindow && now - _quietSince.Value < QuietWindowMs))
            return null;

        _nextDue = now + IntervalMs;
        var turn = _lessonIndex++;
        IdleLesson lesson;
        if (_generated.Count > 0)
            lesson = _generated.Dequeue();
        else if (turn % 5 == 1 && _words.Count >= 3)
        {
            var word = _words[_wordIndex++ % _words.Count];
            lesson = new("遇過的單字 · 間隔複習", word.Word,
                $"{word.Word}＝{word.Meaning}。先回想意思，再跟著念一次。", "review");
        }
        else if (turn % 5 == 3 && config.BuildTipsEnabled)
        {
            var cached = Guides?.Lesson(config, _tipIndex);
            if (cached is not null) { _tipIndex++; lesson = cached; }
            else
            {
                var tips = BuildAdvisor.GetTips(config.PlayerClass, config.BuildPreference);
                lesson = new("一般配裝建議 · 非背包分析", "Build means skills and equipment working together.",
                    tips[_tipIndex++ % tips.Length], "game");
            }
        }
        else
            lesson = NextCurriculum(config);

        lesson = LessonScript.Complete(lesson);
        Remember(lesson);
        return lesson;
    }

    private IdleLesson NextCurriculum(CoachConfig config)
    {
        var pattern = config.LearningFocus switch
        {
            LearningFocusOptions.ToeicFirst => new[] { "toeic", "toeic", "ic", "game" },
            LearningFocusOptions.DigitalIcFirst => new[] { "ic", "ic", "toeic", "game" },
            _ => new[] { "toeic", "ic", "game" }
        };
        var topic = pattern[(_lessonIndex - 1) % pattern.Length];
        return topic switch
        {
            "toeic" => ToeicLessons[_toeicIndex++ % ToeicLessons.Length],
            "ic" => DigitalIcLessons[_icIndex++ % DigitalIcLessons.Length],
            _ => GameLessons[_gameIndex++ % GameLessons.Length]
        };
    }

    private void Remember(IdleLesson lesson)
    {
        _recent.Enqueue(lesson);
        while (_recent.Count > RecentLimit)
            _recent.Dequeue();
    }

    private void LoadGenerated()
    {
        if (_cachePath is null) return;
        try
        {
            if (!File.Exists(_cachePath) || new FileInfo(_cachePath).Length > 256_000) return;
            var saved = JsonSerializer.Deserialize<List<IdleLesson>>(File.ReadAllText(_cachePath)) ?? [];
            foreach (var lesson in saved.TakeLast(GeneratedArchiveLimit).Where(Valid))
            {
                _generatedArchive.Add(lesson);
                _knownGenerated.Add(Signature(lesson.English));
            }
            foreach (var lesson in _generatedArchive.TakeLast(6))
                _generated.Enqueue(lesson);
        }
        catch { /* Optional teaching cache must never stop the overlay. */ }
    }

    private void SaveGenerated()
    {
        if (_cachePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_generatedArchive));
            File.Move(temporary, _cachePath, true);
        }
        catch { /* Personalization still works for this run if persistence fails. */ }
    }

    private static bool Valid(IdleLesson lesson) =>
        lesson.Title is { Length: > 0 and <= 40 } &&
        lesson.English is { Length: >= 3 and <= 180 } &&
        lesson.Chinese is { Length: >= 3 and <= 500 } &&
        lesson.SentenceMeaning is { Length: >= 3 and <= 250 } &&
        lesson.Chinese.Any(character => character is >= '\u3400' and <= '\u9fff');

    private static string Signature(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

    private static bool TooSimilar(string left, string right)
    {
        var a = Signature(left);
        var b = Signature(right);
        if (a == b) return true;
        var aWords = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var bWords = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (aWords.Count == 0 || bWords.Count == 0) return false;
        return aWords.Intersect(bWords).Count() / (double)Math.Max(aWords.Count, bWords.Count) >= .82;
    }
}
