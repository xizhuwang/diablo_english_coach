using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record IdleLesson(string Title, string English, string Chinese, string Topic = "game", string SentenceMeaning = "", string AnchorWord = "");

internal sealed record LessonPersonalizationContext(
    string Topic,
    string RecentLessons,
    string EncounteredWords,
    string AnchorWord = "");

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
    private int _gameIndex;
    private int _topicIndex;
    private IReadOnlyList<GameWordLink> _links = Array.Empty<GameWordLink>();
    private int _lastExtensionTurn = -4;
    private int _contextIndex;

    public void ObserveGameContext(string dialogue, string quest)
    {
        _links = GameContextLessons.Find(dialogue, quest);
        var keep = _generated.Where(Relevant).ToArray();
        _generated.Clear();
        foreach (var lesson in keep) _generated.Enqueue(lesson);
    }

    private bool GameMeaningTaught(string word) => _recent.Any(l => l.AnchorWord == word && l.Topic == "game");

    private bool Relevant(IdleLesson lesson) => _links.Any(l => l.Word == lesson.AnchorWord) &&
        GameContextLessons.Allows(lesson.AnchorWord, lesson.Topic) &&
        GameContextLessons.SafeExample(lesson.AnchorWord, lesson.English, lesson.SentenceMeaning);

    public bool CanPractice(IdleLesson lesson) => string.IsNullOrEmpty(lesson.AnchorWord)
        ? lesson.Topic == "game" : Relevant(lesson);
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
            ? cachePath ?? Path.Combine(CoachConfig.FolderPath, "personalized-lessons-v3.json")
            : null;
        LoadGenerated();
    }

    public bool NeedsPersonalizedLesson => _links.Count > 0 && _generated.Count < GeneratedQueueTarget;

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
        var preferred = config.LearningFocus == LearningFocusOptions.DigitalIcFirst ? "ic" : "toeic";
        var choices = _links.OrderByDescending(l => l.ExtensionTopic == preferred).ToArray();
        var link = choices.Length > 0 ? choices[_topicIndex++ % choices.Length] : null;
        // Preferences only affect naturally available extensions, never invent a link.
        var extend = link is { Extension.Length: > 0 } && _topicIndex % 4 == 0 && GameMeaningTaught(link.Word);
        var topic = extend ? link!.ExtensionTopic : "game";
        var recent = _recent.Count == 0
            ? "none"
            : string.Join(" | ", _recent.TakeLast(8).Select(item => $"{item.Title}: {item.English}"));
        var words = _words.Count == 0
            ? "none yet"
            : string.Join(", ", _words.TakeLast(10).Select(item => $"{item.Word}={item.Meaning}"));
        return new LessonPersonalizationContext(topic, recent, words, link?.Word ?? "");
    }

    public bool AddPersonalizedLesson(IdleLesson lesson)
    {
        var signature = Signature(lesson.English);
        if (!Valid(lesson) || !Relevant(lesson) || _knownGenerated.Contains(signature) ||
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
        // Discard prepared material whose word is no longer on screen/recent.
        while (_generated.Count > 0 && !Relevant(_generated.Peek())) _generated.Dequeue();
        IdleLesson lesson;
        if ((turn % 3 != 2 || !config.BuildTipsEnabled) && _generated.Count > 0 && (_generated.Peek().Topic == "game" ||
            (turn - _lastExtensionTurn >= 4 && GameMeaningTaught(_generated.Peek().AnchorWord))))
            lesson = _generated.Dequeue();
        else if (turn % 5 == 1 && _words.Count >= 3)
        {
            var word = _words[_wordIndex++ % _words.Count];
            lesson = new("遇過的單字 · 間隔複習", word.Word,
                $"{word.Word}＝{word.Meaning}。先回想意思，再跟著念一次。", "review");
        }
        else if (turn % 3 == 2 && config.BuildTipsEnabled)
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
        if (lesson.Topic is "toeic" or "ic") _lastExtensionTurn = turn;
        Remember(lesson);
        return lesson;
    }

    private IdleLesson NextCurriculum(CoachConfig config)
    {
        if (_links.Count > 0)
        {
            var preferred = config.LearningFocus == LearningFocusOptions.DigitalIcFirst ? "ic" : "toeic";
            var choices = _links.OrderByDescending(l => l.ExtensionTopic == preferred).ToArray();
            var link = choices[_contextIndex++ % choices.Length];
            // Explain it IN the game first. Only then is a short shared-word
            // extension eligible, no more often than every four spoken items.
            var extend = GameMeaningTaught(link.Word) && link.Extension.Length > 0 && _lessonIndex - 1 - _lastExtensionTurn >= 4 &&
                !_recent.Any(l => l.AnchorWord == link.Word && l.Topic == link.ExtensionTopic);
            var candidate = GameContextLessons.Create(link, extend);
            if (!_recent.TakeLast(3).Any(l => l.English == candidate.English)) return candidate;
        }
        return GameLessons[_gameIndex++ % GameLessons.Length];
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
        lesson.AnchorWord is { Length: > 0 and <= 40 } &&
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
