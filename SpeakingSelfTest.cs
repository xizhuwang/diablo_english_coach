using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;

namespace DiabloEnglishCoach;

internal static class SpeakingSelfTest
{
    public static async Task<Dictionary<string, object>> PolicyAsync()
    {
        var checks = new Dictionary<string, object>();
        checks["speaking_opt_in_default"] = !new CoachConfig().AutoSpeakingEnabled;
        var planner = new SpeakingPlanner();
        planner.Reset(0);
        checks["speaking_waits_four_minutes"] = planner.TryNext(220_000, true, true) is null;
        checks["speaking_invites_when_quiet"] = planner.TryNext(240_000, true, true) is not null;
        checks["speaking_no_burst"] = planner.TryNext(240_001, true, true) is null;
        checks["speaking_busy_blocks"] = planner.TryNext(600_000, true, false) is null;
        checks["speaking_new_quiet_window"] = planner.TryNext(601_000, true, true) is null;
        checks["speaking_resumes"] = planner.TryNext(616_000, true, true) is not null;
        checks["speaking_disabled_blocks"] = planner.TryNext(900_000, false, true) is null;
        checks["speaking_safe_gate"] = SpeakingPlanner.CanStart(true, true, false, 30, 20000, 20000, 20000);
        checks["speaking_blocks_dialogue"] = !SpeakingPlanner.CanStart(true, true, false, 30, 20000, 20000, 3000);
        checks["speaking_blocks_pending_work"] = !SpeakingPlanner.CanStart(true, true, true, 30, 20000, 20000, 20000);
        checks["speaking_blocks_load"] = !SpeakingPlanner.CanStart(true, true, false, 60, 20000, 20000, 20000);
        checks["speaking_interrupts_space"] = SpeakingPlanner.ShouldInterrupt(true, true, false, 30, 20000, 100);
        checks["speaking_interrupts_combat"] = SpeakingPlanner.ShouldInterrupt(true, true, false, 30, 100, 20000);
        checks["speaking_interrupts_focus"] = SpeakingPlanner.ShouldInterrupt(true, false, false, 30, 20000, 20000);
        checks["speaking_interrupts_off"] = SpeakingPlanner.ShouldInterrupt(false, true, false, 30, 20000, 20000);
        checks["speaking_interrupts_settings"] = SpeakingPlanner.ShouldInterrupt(true, true, true, 30, 20000, 20000);
        checks["speaking_silence_timeout"] = SpeakingRecognitionService.ShouldEndRecording(5, false, 0);
        checks["speaking_endpoint"] = SpeakingRecognitionService.ShouldEndRecording(3.3, true, 2);
        checks["speaking_hard_cap"] = SpeakingRecognitionService.ShouldEndRecording(10, true, 9.9);
        checks["speaking_does_not_cut_active_phrase"] = !SpeakingRecognitionService.ShouldEndRecording(3, true, 2.8);
        checks["speaking_exact_not_pronunciation_score"] = SpeakingFeedback.Describe("I need help.", "i need help").Contains("不代表發音");
        checks["speaking_missing_word"] = SpeakingFeedback.Describe("I need your help.", "i need help").Contains("your");
        checks["speaking_order_matters"] = SpeakingFeedback.Describe("I need help.", "help need i").Contains("可能沒聽清");
        checks["speaking_repeated_word"] = SpeakingFeedback.Describe("Go go now.", "go now").Contains("go");
        checks["speaking_empty_is_skip"] = SpeakingFeedback.Describe("Hello.", "").Contains("先繼續玩");

        var micCalls = 0;
        var decodeCalls = 0;
        var playback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new CancellationTokenSource();
        Task<SpeakingRecording> FakeRecord(CancellationToken _) { micCalls++; return Task.FromResult(new SpeakingRecording([], true)); }
        Task<string> FakeDecode(byte[] _, CancellationToken __) { decodeCalls++; return Task.FromResult("hello"); }
        var pending = SpeakingSession.RunAsync(() => playback.Task, FakeRecord, FakeDecode, _ => { }, cancelled.Token);
        checks["mic_waits_for_playback"] = micCalls == 0;
        cancelled.Cancel();
        try { await pending; checks["speaking_cancel_playback"] = false; }
        catch (OperationCanceledException) { checks["speaking_cancel_playback"] = true; }
        playback.SetResult(true);
        await Task.Delay(20);
        checks["late_playback_never_opens_mic"] = micCalls == 0 && decodeCalls == 0;
        try
        {
            await SpeakingSession.RunAsync(() => Task.FromResult(false), FakeRecord, FakeDecode, _ => { }, CancellationToken.None);
            checks["failed_playback_never_opens_mic"] = false;
        }
        catch (InvalidOperationException) { checks["failed_playback_never_opens_mic"] = micCalls == 0; }

        var stages = new List<bool>();
        var text = await SpeakingSession.RunAsync(() => Task.FromResult(true), FakeRecord, FakeDecode,
            listening => stages.Add(listening), CancellationToken.None);
        checks["speaking_pipeline_order"] = text == "hello" && micCalls == 1 && decodeCalls == 1 && stages.SequenceEqual(new[] { true, false });
        await SpeakingSession.RunAsync(() => Task.FromResult(true),
            _ => Task.FromResult(new SpeakingRecording([], false)), FakeDecode, _ => { }, CancellationToken.None);
        checks["silence_never_loads_model"] = decodeCalls == 1;
        using var duringCapture = new CancellationTokenSource();
        try
        {
            await SpeakingSession.RunAsync(() => Task.FromResult(true), _ =>
            {
                duringCapture.Cancel();
                return Task.FromResult(new SpeakingRecording([], true));
            }, FakeDecode, _ => { }, duringCapture.Token);
            checks["cancel_capture_never_decodes"] = false;
        }
        catch (OperationCanceledException) { checks["cancel_capture_never_decodes"] = decodeCalls == 1; }
        // This call must check cancellation before constructing a microphone device.
        try { await SpeakingRecognitionService.RecordAsync(duringCapture.Token); checks["pre_cancel_no_device"] = false; }
        catch (OperationCanceledException) { checks["pre_cancel_no_device"] = true; }
        return checks;
    }

    // File-only native integration test; never opens a microphone.
    public static async Task<bool> AudioFileAsync(string wavePath, string outputPath)
    {
        var checks = new Dictionary<string, object>();
        try
        {
            using var wave = new WaveFileReader(wavePath);
            if (wave.WaveFormat.SampleRate != 16000 || wave.WaveFormat.Channels != 1 || wave.WaveFormat.BitsPerSample != 16)
                throw new InvalidOperationException("Fixture must be PCM 16 kHz mono 16 bit.");
            var data = new byte[(int)wave.Length];
            wave.ReadExactly(data);
            var clock = Stopwatch.StartNew();
            var text = await SpeakingRecognitionService.RecognizeAsync(data, CancellationToken.None);
            checks["recognized_text"] = text;
            checks["decode_including_model_load_ms"] = clock.ElapsedMilliseconds;
            checks["peak_working_set_mb"] = Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d;
            checks["microphone_used"] = false;
            checks["model_available"] = SpeakingRecognitionService.ModelAvailable;
            var silent = await SpeakingRecognitionService.RecognizeAsync(new byte[32000], CancellationToken.None);
            checks["silence_not_forced_to_target"] = string.IsNullOrWhiteSpace(silent);
            checks["passed"] = text.Contains("need", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("help", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(silent);
        }
        catch (Exception ex) { checks["passed"] = false; checks["error"] = ex.ToString(); }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks["passed"] is true;
    }
}
