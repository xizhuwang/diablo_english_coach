using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record GameWordLink(string Word, string Pattern, string English, string Meaning,
    string Usage, string Extension = "", string ExtensionTopic = "toeic");

// A lexical connection is required; combat is NOT an excuse to teach arbitrary
// engineering concepts. In particular, cooldown is never equated with setup time.
internal static class GameContextLessons
{
    private static readonly GameWordLink[] Links =
    [
        new("increase", @"increas(?:e|es|ed|ing)", "This effect increases skill damage.",
            "這個效果會增加技能傷害。", "increase 是增加；先看它增加的是哪個數值。",
            "這個字也常出現在多益：increase sales 是增加銷售額。動詞相同，後面的對象不同。"),
        new("compare", @"compar(?:e|es|ed|ing)", "Compare the two items.", "比較這兩件物品。",
            "compare 是比較，選裝時要看屬性和技能效果。",
            "工作上也會說 compare prices，比較價格；還是同一個 compare。"),
        new("require", @"requir(?:e|es|ed|ing)", "This item requires a higher level.", "這件物品需要更高的等級。",
            "requires 後面是必要條件；不符合條件時通常還不能使用。",
            "多益職缺裡的 require experience 是需要經驗，也是指出必要條件。"),
        new("available", @"available", "The skill is available now.", "現在可以使用這個技能。",
            "available 表示現在可用；有 not 時意思就相反。",
            "工作上說 a room is available，是房間可使用；核心意思一樣是可用。"),
        new("confirm", @"confirm(?:s|ed|ing)?", "Confirm your choice.", "確認你的選擇。",
            "confirm 是確認；按介面上的確認鍵前，先看清楚選擇。",
            "郵件中的 confirm the time 是確認時間；和確認遊戲選擇是同一種用法。"),
        new("avoid", @"avoid(?:s|ed|ing)?", "Avoid the incoming attack.", "避開即將到來的攻擊。",
            "avoid 是避開，後面直接接要避開的事物。",
            "多益裡也有 avoid delays，避免延誤；和避開攻擊共用 avoid。"),
        new("return", @"return(?:s|ed|ing)?", "Return to the gate.", "回到大門。",
            "return to 表示回到某處。",
            "工作上說 return to the office，就是回到辦公室；保留 return to 這個搭配。"),
        new("reset", @"reset(?:s|ting)?", "Reset your skills.", "重置你的技能。",
            "reset 是重置，your skills 是你的技能。",
            "數位 IC 也用 reset 表示重置，讓電路進入已知狀態。", "ic"),
        new("cooldown", @"cooldown", "Wait for the cooldown.", "等待冷卻時間結束。",
            "cooldown 是冷卻時間，技能還沒恢復時可先走位。"),
        new("damage", @"damage", "The skill deals area damage.", "這個技能會造成範圍傷害。",
            "deal damage 是造成傷害；area damage 是範圍傷害。"),
        new("head to", @"head\s+to", "Head to the marked area.", "前往標記的區域。",
            "head to 是前往；後面的地點才是你要找的目標。"),
        new("follow", @"follow(?:s|ed|ing)?", "Follow the guide.", "跟著引導者走。",
            "follow 是跟隨；看它後面接誰，就知道句子在指哪個對象。"),
        new("search for", @"search(?:es|ed|ing)?\s+for", "Search for the missing item.", "尋找遺失的物品。",
            "search for 後面接要找的目標。先理解要找什麼，不要只記 search。"),
        new("head forward", @"head\s+forward", "Head forward along the path.", "沿著路往前走。",
            "head forward 是往前走；along the path 是沿著路。"),
        new("talk to", @"talk(?:s|ed|ing)?\s+to", "Talk to the guard.", "和守衛交談。",
            "talk to 後面接交談的對象；和擊敗目標是不同的任務動作。"),
        new("stay close", @"stay\s+close", "Stay close to me.", "待在我附近。",
            "stay close to 是保持靠近；to 後面指出要靠近誰。"),
        new("defeat", @"defeat(?:s|ed|ing)?", "Defeat the enemies in the area.", "擊敗區域內的敵人。",
            "defeat 是擊敗；後面接要擊敗的目標。"),
        new("equip", @"equip(?:s|ped|ping)?", "Equip the new item.", "裝備新物品。",
            "equip 是把物品裝備上去，不是只放進背包。"),
        new("upgrade", @"upgrad(?:e|es|ed|ing)", "Upgrade your equipment.", "升級你的裝備。",
            "upgrade 是提升等級或品質，equipment 是裝備。"),
        new("leave", @"leav(?:e|es|ing)", "Leave the dungeon.", "離開地城。",
            "leave 是離開，後面可直接接地點，不用加 to。")
    ];

    public static IReadOnlyList<GameWordLink> Find(string dialogue, string quest) => Links.Where(link =>
        Matches(dialogue, link.Pattern) || Matches(quest, link.Pattern)).ToArray();

    private static bool Matches(string source, string pattern) =>
        OcrService.LooksLikeEnglishSubtitle(source) && Regex.IsMatch(source, @"\b(?:" + pattern + @")\b", RegexOptions.IgnoreCase);

    public static bool ContainsWord(string source, string word) => Links.FirstOrDefault(l => l.Word == word) is { } link &&
        Regex.IsMatch(source, @"\b(?:" + link.Pattern + @")\b", RegexOptions.IgnoreCase);

    public static IdleLesson Create(GameWordLink link, bool extend) => new("從遊戲學英文", link.English,
        link.Usage + (extend ? link.Extension : ""), extend ? link.ExtensionTopic : "game", link.Meaning, link.Word);

    public static bool Allows(string word, string topic) => Links.Any(l => l.Word == word &&
        (topic == "game" || (l.Extension.Length > 0 && l.ExtensionTopic == topic)));

    public static string Usage(string word) => Links.FirstOrDefault(l => l.Word == word)?.Usage ?? "";

    public static bool SafeExample(string word, string english, string translation)
    {
        if (!ContainsWord(english, word)) return false;
        // Observed small-model error: cooldown is not charging, nor a lock on all actions.
        return word != "cooldown" || (!Regex.IsMatch(translation, "充能|充電|蓄力|不能行動|無法行動|其他動作|必須") &&
            !Regex.IsMatch(english, @"\b(charge|charging|cannot|unable|must)\b", RegexOptions.IgnoreCase));
    }

    public static string Introduction(IdleLesson lesson)
    {
        var link = Links.FirstOrDefault(l => l.Word == lesson.AnchorWord);
        if (link is null) return "練一句遊戲英文。";
        if (lesson.Topic == "game") return $"接著剛才遊戲裡的 {link.Word}。";
        return $"延續剛才遊戲裡的 {link.Word}，再學一個用法。";
    }
}
