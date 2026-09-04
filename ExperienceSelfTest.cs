namespace DiabloEnglishCoach;

internal static class ExperienceSelfTest
{
    public static Dictionary<string, object> Run()
    {
        static string Dialogue(params string[] lines) => DialogueText.Normalize(lines.Select(l => new SubtitleLine(l)), true);
        var tests = new Dictionary<string, object>
        {
            ["ocr_removes_separate_speaker"] = Dialogue("Deckard Cain", "I need y0ur help.") == "I need your help.",
            ["ocr_removes_colon_speaker"] = Dialogue("Xul: F0ll0w me.") == "Follow me.",
            ["ocr_keeps_name_inside_sentence"] = Dialogue("Deckard Cain needs your help.") == "Deckard Cain needs your help.",
            ["ocr_keeps_wrapped_subject"] = Dialogue("Cain", "needs your help.") == "Cain needs your help.",
            ["ocr_keeps_short_utterance"] = Dialogue("Stay close", "to me.") == "Stay close to me.",
            ["ocr_quest_heading_preserved"] = DialogueText.Normalize(new[] { new SubtitleLine("Deckard Cain"), new SubtitleLine("Talk to Cain.") }, false).StartsWith("Deckard Cain"),
            ["ocr_unknown_name_geometry"] = DialogueText.Normalize(new[] { new SubtitleLine("Arlen", 0, 10, 35), new SubtitleLine("I need your help.", 22, 10, 150) }, true) == "I need your help.",
            ["ocr_unknown_name_no_geometry_kept"] = Dialogue("Arlen", "I need your help.").StartsWith("Arlen"),
            ["ocr_zero_words_fixed"] = DialogueText.RepairZeroes("Y0U need y0ur b0ne arm0r n0w.") == "YOU need your bone armor now.",
            ["ocr_numbers_codes_preserved"] = DialogueText.RepairZeroes("10% 2/5 100 gold RTX3050 B0 0x10 10K 5s") == "10% 2/5 100 gold RTX3050 B0 0x10 10K 5s",
            ["traditional_converter"] = TraditionalText.Convert("这个技能会造成伤害，装备升级。")== "這個技能會造成傷害，裝備升級。",
            ["traditional_preserves_english_numbers"] = TraditionalText.Convert("Soulfire 10% 2/5") == "Soulfire 10% 2/5",
            ["traditional_stream_cleaned"] = FastTranslationService.CleanLocalModelReply("The skill deals damage.", "这个技能会造成伤害。") == "這個技能會造成傷害。",
            ["dialogue_detects_conversation"] = OcrService.LooksLikeCharacterDialogue("I need your help. Follow me."),
            ["dialogue_detects_question"] = OcrService.LooksLikeCharacterDialogue("Where is the shard?"),
            ["dialogue_ignores_action_button"] = !OcrService.LooksLikeCharacterDialogue("Leave Mad King's Breach"),
            ["dialogue_ignores_quest_imperative"] = !OcrService.LooksLikeCharacterDialogue("Head forward and search for Leoric"),
            ["speaking_moderate_load_allowed"] = SpeakingPlanner.CanStart(true, true, false, 60, 9000, 9000, 9000),
            ["speaking_recent_action_blocked"] = !SpeakingPlanner.CanStart(true, true, false, 30, 500, 9000, 9000)
        };
        var planner = new SpeakingPlanner { IntervalMilliseconds = 45_000 };
        planner.Reset(0, initial: true);
        planner.TryNext(27_000, true, true);
        var first = planner.TryNext(30_000, true, true);
        planner.TryNext(72_000, true, true);
        var second = planner.TryNext(75_000, true, true);
        tests["speaking_first_invitation_thirty_seconds"] = first is not null && !first.Recall;
        tests["speaking_second_is_active_recall"] = second is { Recall: true };
        tests["speaking_configurable_interval"] = planner.RemainingMs(75_000) == 45_000;
        return tests;
    }
}
