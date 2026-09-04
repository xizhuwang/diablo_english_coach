namespace DiabloEnglishCoach;

// Narrow compatibility cleanup for old cached lessons and known model boilerplate.
// Never remove arbitrary negative/conditional sentences: those can carry real facts.
internal static class SpokenStyle
{
    private static readonly (string Old, string New)[] Replacements =
    [
        ("英文練習（不是新任務）：", "英文用法："),
        ("實際效果以遊戲為準。", ""),
        ("實際效果請以遊戲內為準。", ""),
        ("實際效果以遊戲內為準。", ""),
        ("僅供參考。", ""),
        ("這是例句，不是新的任務。", ""),
        ("這裡只是英文例句，不是建議你現在重配技能。", ""),
        ("這是同字的另一個用途，不是說遊戲和電路有相同機制。", ""),
        ("這是同字的電路用法，不表示遊戲和電路機制相同。", ""),
        ("這句只是示範。", ""),
        ("這不是新任務。", ""),
        ("這是示範，不是新路線。", ""),
        ("這是例句，不是換裝指示。", ""),
        ("這裡是學英文，不是要你花資源。", ""),
        ("這只是示範句。", ""),
        ("這句只是任務英文的示範。", ""),
        ("這是文字核對，不代表發音評分。", "")
    ];

    internal static string Clean(string text)
    {
        foreach (var (old, replacement) in Replacements)
            text = text.Replace(old, replacement, StringComparison.Ordinal);
        return TraditionalText.Convert(text.Trim());
    }
}
