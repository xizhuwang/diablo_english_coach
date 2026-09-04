namespace DiabloEnglishCoach;

internal sealed record IdleLesson(string Title, string English, string Chinese);

// A small offline curriculum: no screenshot expansion, network search, or LLM
// generation. Clock values are supplied by the caller to make timing testable.
internal sealed class IdleLessonPlanner
{
    public BuildGuideCache? Guides { get; set; }
    internal const long IntervalMs = 1_000;
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
        new("流派入門", "Keep a defensive option.", "defensive＝防禦性的。搭配主要傷害技能時，可留一個保命或移動手段。"),
        new("任務英文", "Reach the marked area.", "reach＝抵達；marked area＝標記區域。先找地圖或地面的任務標記。這是英文範例，不是目前任務。"),
        new("戰鬥英文", "Keep your distance.", "keep your distance＝保持距離。遠程角色可讓召喚物或隊友先接觸敵人。"),
        new("掉落英文", "Pick up the loot.", "pick up＝撿起；loot＝戰利品。loot 在遊戲裡通常是裝備或材料。"),
        new("技能英文", "The skill deals area damage.", "deal damage＝造成傷害；area damage＝範圍傷害。適合處理聚集的怪物。"),
        new("裝備英文", "Equip the better item.", "equip＝裝備；better＝較好的。先確認綠色箭頭，再讀傳奇效果是否配合常用技能。"),
        new("任務英文", "Interact with the object.", "interact with＝與……互動。看到這個片語時，通常要靠近並按互動鍵。這不是目前任務。"),
        new("資源英文", "You do not have enough energy.", "enough＝足夠的；energy＝能量。資源不足時先用不消耗該資源的攻擊。"),
        new("戰鬥英文", "Avoid the incoming attack.", "avoid＝避開；incoming＝即將到來的。看到地面提示時先移動，再繼續輸出。"),
        new("裝備英文", "This bonus affects your summons.", "bonus＝加成；affect＝影響；summons＝召喚物。確認加成對象和你的核心玩法相同。"),
        new("介面英文", "Open your inventory.", "inventory＝背包。open your inventory 是「打開背包」；這是常見介面指示。"),
        new("任務英文", "Complete the objective.", "complete＝完成；objective＝目標。先找句子中的動詞，就知道下一步行動。"),
        new("流派入門", "Test the build in combat.", "test＝測試；in combat＝在戰鬥中。換技能後實際打一小段，再判斷是否更順手。")
    ];

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

        _nextDue = now + IntervalMs; // No backlog: never dump missed lessons.
        var turn = _lessonIndex++;
        // Do not repeat a single captured word every few paragraphs. Wait until
        // there is a useful mini-deck, then interleave it at a lower frequency.
        if (turn % 4 == 1 && _words.Count >= 3)
        {
            var word = _words[_wordIndex++ % _words.Count];
            return new("剛才的單字 · 複習", word.Word, $"{word.Word}＝{word.Meaning}。試著先想起意思，再看提示。");
        }
        if (turn % 4 == 3 && config.BuildTipsEnabled)
        {
            var cached = Guides?.Lesson(config, _tipIndex);
            if (cached is not null) { _tipIndex++; return cached; }
            var tips = BuildAdvisor.GetTips(config.PlayerClass, config.BuildPreference);
            return new("一般配裝建議 · 非背包分析", "Build = 技能與裝備的搭配", tips[_tipIndex++ % tips.Length]);
        }
        return Lessons[_curriculumIndex++ % Lessons.Length];
    }
}
