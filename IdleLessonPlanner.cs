namespace DiabloEnglishCoach;

internal sealed record IdleLesson(string Title, string English, string Chinese);

// A small offline curriculum: no screenshot expansion, network search, or LLM
// generation. Clock values are supplied by the caller to make timing testable.
internal sealed class IdleLessonPlanner
{
    internal const long IntervalMs = 45_000;
    internal const long QuietWindowMs = 10_000;
    private long _nextDue;
    private long? _quietSince;
    private int _lessonIndex;
    private int _curriculumIndex;
    private int _wordIndex;
    private int _tipIndex;
    private readonly List<KeywordCard> _words = new();

    private static readonly IdleLesson[] Lessons =
    [
        new("任務英文", "Head to the gate.", "head to＝前往。例句：前往大門。這是語言練習，不是你的下一個任務。"),
        new("裝備英文", "Compare the two items.", "compare＝比較；item＝物品。選裝時先比較同一欄位的屬性與技能效果。"),
        new("流派入門", "Choose one core skill.", "core skill＝核心技能。先圍繞常用技能搭配裝備，不必一直更換玩法。"),
        new("對話英文", "We need your help.", "need＝需要。need your help 是「需要你的幫忙」；留意對話中說話者要你做什麼。"),
        new("戰鬥英文", "Wait for the cooldown.", "cooldown＝冷卻時間。技能尚未恢復時，先走位、保持安全距離。"),
        new("裝備英文", "Damage or survivability?", "damage＝傷害；survivability＝生存能力。常倒下時先補生存，推進穩定後再試傷害配置。"),
        new("對話英文", "Return before it is too late.", "return＝返回；before＝在……之前。這是練習句，不代表接下來的劇情。"),
        new("技能英文", "This effect increases skill damage.", "effect＝效果；increase＝增加。看清效果是否強化你正在使用的技能。"),
        new("裝備比較", "Change one item at a time.", "at a time＝每次。每次只換一件，再觀察清怪與生存，才容易知道改動是否有幫助。"),
        new("對話英文", "Follow me. Stay close.", "follow＝跟隨；stay close＝保持靠近。先抓動詞，就比較容易懂任務指示。"),
        new("選裝習慣", "Read the full description.", "description＝說明。先讀完整效果再決定；沒有看到兩件裝備的數值，不能判定哪件更好。"),
        new("流派入門", "Keep a defensive option.", "defensive＝防禦性的。搭配主要傷害技能時，可留一個保命或移動手段。")
    ];

    public void Reset(long now)
    {
        _nextDue = now + IntervalMs;
        _quietSince = null;
    }

    public void ObserveLesson(long now, IReadOnlyList<KeywordCard> words)
    {
        Reset(now);
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

    public IdleLesson? TryNext(long now, bool safeToTeach, CoachConfig config)
    {
        if (!safeToTeach)
        {
            _quietSince = null;
            return null;
        }
        _quietSince ??= now;
        if (now < _nextDue || now - _quietSince.Value < QuietWindowMs)
            return null;

        _nextDue = now + IntervalMs; // No backlog: never dump missed lessons.
        var turn = _lessonIndex++;
        if (turn % 3 == 1 && _words.Count > 0)
        {
            var word = _words[_wordIndex++ % _words.Count];
            return new("剛才的單字 · 複習", word.Word, $"{word.Word}＝{word.Meaning}。試著先想起意思，再看提示。");
        }
        if (turn % 3 == 2 && config.BuildTipsEnabled)
        {
            var tips = BuildAdvisor.GetTips(config.PlayerClass, config.BuildPreference);
            return new("一般配裝建議 · 非背包分析", "Build = 技能與裝備的搭配", tips[_tipIndex++ % tips.Length]);
        }
        return Lessons[_curriculumIndex++ % Lessons.Length];
    }
}
