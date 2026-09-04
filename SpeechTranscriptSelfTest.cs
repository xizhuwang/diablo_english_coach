namespace DiabloEnglishCoach;

internal static class SpeechTranscriptSelfTest
{
    public static async Task<Dictionary<string, object>> RunAsync()
    {
        var checks = new Dictionary<string, object>();
        var transcript = new SpeechTranscript();
        checks["transcript_hidden_before_playback"] = !transcript.Visible(0) && transcript.History.Count == 0;
        const string exact = "接著剛才的 increase。This effect increases skill damage. 這個效果會增加技能傷害。";
        transcript.Observe(new(1, exact, true), 10);
        checks["transcript_exact_tts_input"] = transcript.Current?.Text == exact && transcript.FullText().Contains(exact);
        checks["transcript_stays_during_long_playback"] = transcript.Visible(100_000);
        transcript.Observe(new(1, exact, false, true), 100_000);
        checks["transcript_reading_grace_period"] = transcript.Visible(107_999) && !transcript.Visible(108_000);
        checks["transcript_finished_kept_in_history"] = transcript.FullText().Contains("已播完") && transcript.FullText().Contains(exact);
        transcript.Observe(new(2, "I need your help.", true), 110_000);
        transcript.Observe(new(1, exact, false), 110_001);
        checks["transcript_old_completion_cannot_hide_new_speech"] = transcript.Playing && transcript.Current?.Id == 2;
        transcript.Observe(new(2, "I need your help.", false), 110_002);
        checks["transcript_interruption_marked"] = transcript.Current?.Status == "已停止";
        transcript.Observe(new(1, "stale", true), 110_003);
        checks["transcript_ignores_stale_start"] = transcript.Current?.Id == 2;
        for (var i = 3; i <= 45; i++) transcript.Observe(new(i, $"lesson {i}", true), i);
        checks["transcript_bounded_history"] = transcript.History.Count == 30 && transcript.History[0].Id == 16;

        // No real speech, model, microphone or network: rejected playback has no transcript.
        using var speech = new SpeechService(new CoachConfig { UseOnlineNeuralVoice = false }, initializeLocalVoice: false);
        var events = 0;
        speech.PlaybackChanged += _ => events++;
        checks["failed_audio_has_no_transcript"] = !await speech.SpeakEnglishAndWaitAsync("No voice installed.") && events == 0;
        speech.MayStartPlayback = () => false;
        checks["blocked_audio_has_no_transcript"] = !await speech.SpeakEnglishAndWaitAsync("Game dialogue is active.") && events == 0;
        return checks;
    }
}
