namespace DiabloEnglishCoach;

internal static class BuildAdvisor
{
    public static CoachReply AppendTip(CoachReply reply, CoachConfig config, string visibleText, BuildGuideCache? guides = null)
    {
        if (!config.BuildTipsEnabled)
            return reply;

        var card = guides?.Lesson(config, StableIndex(visibleText, 4));
        if (card is not null)
            return reply with { Advice = $"{card.Title}\n{card.English}：{card.Chinese}" };

        var tips = GetTips(config.PlayerClass, config.BuildPreference);
        var index = StableIndex(visibleText, tips.Length);
        return reply with
        {
            Advice = tips[index]
        };
    }

    internal static string[] GetTips(string playerClass, string preference)
    {
        if (playerClass.Equals("Necromancer", StringComparison.OrdinalIgnoreCase))
        {
            return preference switch
            {
                "Summons" =>
                [
                    "流派提示：以 summons（召喚物）為核心，優先試用明確強化骷髏、法師或魔像的傳奇精華。",
                    "技能提示：召喚流先保留一個保命或位移技能，其餘位置集中強化召喚物，操作會比較輕鬆。",
                    "裝備提示：兩件裝備接近時，優先選能強化你目前召喚技能的 Legendary effect（傳奇效果）。"
                ],
                "Survival" =>
                [
                    "生存提示：如果常被打倒，先補 Life、防護或移動能力，再追求更高輸出。",
                    "流派提示：讓召喚物吸引敵人，自己保持距離；裝備優先配合你真正有在使用的技能。",
                    "裝備提示：Damage 很誘人，但能讓你穩定完成任務的生存效果通常更適合目前階段。"
                ],
                "Damage" =>
                [
                    "傷害流提示：集中強化一組主要輸出技能，優先測試能增加 Damage、攻速或縮短冷卻的相關效果。",
                    "裝備提示：傳奇效果若沒有強化你正在使用的技能，即使看起來華麗也不一定比較適合。",
                    "技能提示：召喚物負責持續輸出時，可搭配一個清理群怪的技能，推主線會更順。"
                ],
                _ =>
                [
                    "輕鬆流派：若已解鎖 Command Skeletons，可先圍繞 summons（召喚物）組合技能。",
                    "裝備提示：前期可先參考遊戲的升級標記；傳奇裝備則優先保留強化常用技能的精華。",
                    "選裝原則：先確定一個主要輸出方向，再選能同時補 Damage、Life 或核心技能效果的裝備。"
                ]
            };
        }

        return
        [
            "流派提示：先選一個最常用的主要傷害技能，其他技能補移動、控制與生存，會比平均分散更容易上手。",
            "裝備提示：前期可先參考升級標記；遇到傳奇裝備時，優先看效果是否強化你真正使用的技能。",
            "選裝原則：卡關時先補 Life 與防護，推進順利時再增加 Damage；不必只追求單一數字。"
        ];
    }

    private static int StableIndex(string text, int count)
    {
        var hash = 17;
        foreach (var character in text)
            hash = unchecked(hash * 31 + char.ToLowerInvariant(character));
        return (hash & int.MaxValue) % count;
    }
}
