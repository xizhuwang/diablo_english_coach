using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record GameWordLink(string Word, string Pattern, string English, string Meaning,
    string Usage, string Extension = "", string ExtensionTopic = "toeic",
    string ExtensionEnglish = "", string ExtensionMeaning = "");

// A lexical connection is required; combat is NOT an excuse to teach arbitrary
// engineering concepts. In particular, cooldown is never equated with setup time.
internal static class GameContextLessons
{
    private static readonly GameWordLink[] Links =
    [
        new("increase", @"increas(?:e|es|ed|ing)", "This effect increases skill damage.",
            "這個效果會增加技能傷害。", "increase 是增加；先看它增加的是哪個數值。",
            "increase sales 是增加銷售額；遊戲和職場都是『提高某個數值』。", "toeic",
            "Sales increased significantly this quarter.", "本季銷售額顯著增加。"),
        new("compare", @"compar(?:e|es|ed|ing)", "Compare the two items.", "比較這兩件物品。",
            "compare 是比較，選裝時要看屬性和技能效果。",
            "compare prices 是比較價格；先找 compare 後面的兩個對象。", "toeic",
            "Please compare the available plans.", "請比較現有的方案。"),
        new("require", @"requir(?:e|es|ed|ing)", "This item requires a higher level.", "這件物品需要更高的等級。",
            "requires 後面是必要條件；不符合條件時通常還不能使用。",
            "require experience 是需要經驗；require 後面接必要條件。", "toeic",
            "The position requires relevant experience.", "這個職位需要相關經驗。"),
        new("available", @"available", "The skill is available now.", "現在可以使用這個技能。",
            "available 表示現在可用；有 not 時意思就相反。",
            "a room is available 表示房間可以使用；核心意思仍是『可用』。", "toeic",
            "The conference room is available this afternoon.", "會議室今天下午可以使用。"),
        new("confirm", @"confirm(?:s|ed|ing)?", "Confirm your choice.", "確認你的選擇。",
            "confirm 是確認；按介面上的確認鍵前，先看清楚選擇。",
            "confirm the schedule 是確認時程；和確認遊戲選擇是同一種用法。", "toeic",
            "Please confirm the delivery schedule.", "請確認交貨時程。"),
        new("avoid", @"avoid(?:s|ed|ing)?", "Avoid the incoming attack.", "避開即將到來的攻擊。",
            "avoid 是避開，後面直接接要避開的事物。",
            "avoid delays 是避免延誤；和避開攻擊共用 avoid。", "toeic",
            "Submit the form early to avoid delays.", "請提早交表格以避免延誤。"),
        new("return", @"return(?:s|ed|ing)?", "Return to the gate.", "回到大門。",
            "return to 表示回到某處。",
            "return the signed form 是交回簽好的表格；這裡 return 是及物動詞。", "toeic",
            "Please return the signed form by Friday.", "請在星期五前交回簽好的表格。"),
        new("reset", @"reset(?:s|ting)?", "Reset your skills.", "重置你的技能。",
            "reset 是重置，your skills 是你的技能。",
            "數位 IC 也用 reset 表示重置，讓電路進入已知狀態。", "ic",
            "Reset places the design in a known state.", "重置會讓設計進入已知狀態。"),
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
            "leave 是離開，後面可直接接地點，不用加 to。"),
        new("complete", @"complet(?:e|es|ed|ing)", "Complete the current objective.", "完成目前的目標。",
            "complete 是完成；先確認後面要完成的是任務、事件還是收集數量。",
            "complete the survey 是完成問卷；多益常用 complete 加工作項目。", "toeic",
            "Please complete the survey by Friday.", "請在星期五前完成問卷。"),
        new("receive", @"receiv(?:e|es|ed|ing)", "You will receive a reward.", "你會獲得獎勵。",
            "receive 是收到；遊戲裡後面常接 reward 或 item。",
            "receive an email 是收到電子郵件；先找收到的東西。", "toeic",
            "You will receive a confirmation email.", "你會收到一封確認信。"),
        new("select", @"select(?:s|ed|ing)?", "Select one reward.", "選擇一項獎勵。",
            "select 是選擇；後面的名詞是可選項目。",
            "select an option 是選擇一個選項；介面與多益都常見。", "toeic",
            "Select the preferred delivery option.", "請選擇偏好的配送方式。"),
        new("provide", @"provid(?:e|es|ed|ing)", "This item provides extra armor.", "這件物品提供額外護甲。",
            "provide 是提供；先看提供了什麼效果。",
            "provide information 是提供資料；provide 後面接提供的內容。", "toeic",
            "Please provide the requested information.", "請提供要求的資料。"),
        new("replace", @"replac(?:e|es|ed|ing)", "Replace the weaker item.", "替換較弱的物品。",
            "replace 是替換；選裝時比較新舊裝備的核心效果。",
            "replace damaged equipment 是更換損壞設備。", "toeic",
            "We will replace the damaged equipment.", "我們會更換損壞的設備。"),
        new("reduce", @"reduc(?:e|es|ed|ing)", "This effect reduces damage taken.", "這個效果會降低承受的傷害。",
            "reduce 是降低；後面指出被降低的數值。",
            "reduce costs 是降低成本；遊戲和職場都在描述數值下降。", "toeic",
            "The new process reduces operating costs.", "新流程會降低營運成本。"),
        new("collect", @"collect(?:s|ed|ing)?", "Collect the quest items.", "收集任務物品。",
            "collect 是收集；任務後面通常會顯示物品和數量。",
            "collect documents 是領取或收集文件。", "toeic",
            "Please collect the documents at reception.", "請到接待處領取文件。"),
        new("purchase", @"purchas(?:e|es|ed|ing)", "Purchase the item from the merchant.", "向商人購買物品。",
            "purchase 是購買，比 buy 正式；先確認價格和物品。",
            "purchase tickets 是購票；多益常見於通知和網站說明。", "toeic",
            "Customers can purchase tickets online.", "顧客可以在線上購票。"),
        new("improve", @"improv(?:e|es|ed|ing)", "Improve your main skill first.", "先強化你的主要技能。",
            "improve 是改善或提升；後面接要改善的能力。",
            "improve efficiency 是提升效率。", "toeic",
            "The new process improves efficiency.", "新流程會提升效率。")
    ];

    public static IReadOnlyList<GameWordLink> Find(string dialogue, string quest) => Links.Where(link =>
        Matches(dialogue, link.Pattern) || Matches(quest, link.Pattern)).ToArray();

    private static bool Matches(string source, string pattern) =>
        OcrService.LooksLikeEnglishSubtitle(source) && Regex.IsMatch(source, @"\b(?:" + pattern + @")\b", RegexOptions.IgnoreCase);

    public static bool ContainsWord(string source, string word) => Links.FirstOrDefault(l => l.Word == word) is { } link &&
        Regex.IsMatch(source, @"\b(?:" + link.Pattern + @")\b", RegexOptions.IgnoreCase);

    public static IdleLesson Create(GameWordLink link, bool extend) => extend && link.ExtensionEnglish.Length > 0
        ? new("從遊戲延伸英文", link.ExtensionEnglish, link.Extension,
            link.ExtensionTopic, link.ExtensionMeaning, link.Word)
        : new("從遊戲學英文", link.English, link.Usage, "game", link.Meaning, link.Word);

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
