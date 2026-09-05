using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record ScreenPurpose(string Key, string Chinese, string Keyword, string Meaning, string EnglishTip);

// Decide whether stable HUD text is useful before spending translation/model
// time. The coach reads the purpose of an objective/control, not every OCR
// variation of the same persistent panel.
internal static class ScreenTextPolicy
{
    public static bool IsLikelyInterfaceText(string text) =>
        Regex.IsMatch(text, @"^(?:Click|Press|Tap|Hold)\b", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(text, @"^(?:Head|Go|Leave|Return|Talk|Search|Defeat|Collect|Equip|Use|Upgrade|Enter|Open|Claim|Select)\b",
            RegexOptions.IgnoreCase) && !Regex.IsMatch(text, @"\b(?:I|me|my|we|us|our|you|your)\b", RegexOptions.IgnoreCase);

    public static string Identity(string text)
    {
        text = OcrService.Clean(text).ToLowerInvariant();
        text = Regex.Replace(text, @"\b\d+\s*/\s*\d+\b|\b\d+\b", "#");
        return Regex.Replace(text, @"[^a-z#]+", " ").Trim();
    }

    public static bool SamePurpose(string left, string right) =>
        left.Length > 0 && Identity(left) == Identity(right);

    public static ScreenPurpose? Explain(string text)
    {
        var clean = OcrService.Clean(text).Trim();
        if (Regex.IsMatch(clean, @"click\s+to\s+stop\s+tracking\s+quests?", RegexOptions.IgnoreCase))
            return new(Identity(clean), "用途：任務追蹤開關。點擊後停止在畫面追蹤這項任務，不是放棄任務。",
                "track", "追蹤", "stop tracking 表示停止追蹤。" );

        var combined = Regex.Match(clean, @"\bhead\s+forward\s+and\s+search\s+for\s+(.+?)(?:[.!?]|$)", RegexOptions.IgnoreCase);
        if (combined.Success)
            return new(Identity(clean), $"用途：探索目標。先沿目前路線前進，再尋找 {Target(combined)}。",
                "search for", "尋找", "search for 後面接要尋找的目標。" );

        var patterns = new (string Pattern, string Purpose, string Action, string Word, string Meaning, string Tip)[]
        {
            (@"\bhead\s+to\s+(.+?)(?:[.!?]|$)", "導航目標", "前往", "head to", "前往", "head to 後面接目的地。"),
            (@"\bgo\s+to\s+(.+?)(?:[.!?]|$)", "導航目標", "前往", "go to", "前往", "go to 後面接目的地。"),
            (@"\btalk\s+to\s+(.+?)(?:[.!?]|$)", "推進對話", "與", "talk to", "與……交談", "talk to 後面接交談對象。"),
            (@"\bsearch\s+for\s+(.+?)(?:[.!?]|$)", "探索目標", "尋找", "search for", "尋找", "search for 後面接要尋找的目標。"),
            (@"\bfind\s+(.+?)(?:[.!?]|$)", "探索目標", "尋找", "find", "找到／尋找", "find 強調找到目標。"),
            (@"\bdefeat\s+(.+?)(?:[.!?]|$)", "戰鬥目標", "擊敗", "defeat", "擊敗", "defeat 後面接要擊敗的對象。"),
            (@"\bcollect\s+(.+?)(?:[.!?]|$)", "收集目標", "收集", "collect", "收集", "collect 後面接要收集的物品。"),
            (@"\bfollow\s+(.+?)(?:[.!?]|$)", "移動目標", "跟隨", "follow", "跟隨", "follow 後面接跟隨對象。"),
            (@"\benter\s+(.+?)(?:[.!?]|$)", "區域入口", "進入", "enter", "進入", "enter 可直接接地點。"),
            (@"\bleave\s+(.+?)(?:[.!?]|$)", "區域出口", "離開", "leave", "離開", "leave 可直接接地點。"),
            (@"\breturn\s+to\s+(.+?)(?:[.!?]|$)", "返回目標", "返回", "return to", "返回", "return to 後面接返回地點。")
        };

        foreach (var item in patterns)
        {
            var match = Regex.Match(clean, item.Pattern, RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var target = Target(match);
            var chinese = item.Word == "talk to"
                ? $"用途：{item.Purpose}。現在與 {target} 交談，讓目前事件繼續。"
                : $"用途：{item.Purpose}。現在要{item.Action} {target}。";
            return new(Identity(clean), chinese, item.Word, item.Meaning, item.Tip);
        }
        return null;
    }

    private static string Target(Match match) =>
        Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
}
