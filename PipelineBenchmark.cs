using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace DiabloEnglishCoach;

internal static class PipelineBenchmark
{
    public static async Task<bool> RunAsync(string output)
    {
        var samples = new[] {
            "Your summons deal 20% more damage.",
            "Do not open the gate until I return.",
            "I need your help. Follow me and stay close.",
            "The skill is not ready yet. Wait for the cooldown.",
            "Defeat the undead and find the shard.",
            "Leoric is waiting at the gate."
        };
        var ocr = new OcrService();
        var config = new CoachConfig { TranslationProvider = TranslationProviders.LocalCpu, InferenceThreads = 1 };
        var cache = Path.ChangeExtension(output, ".cache.json");
        using var service = new FastTranslationService(cachePath: cache);
        var warm = await service.WarmAsync(config, default);
        var rows = new List<object>();
        var passed = warm.Text is not null;
        foreach (var sample in samples)
        {
            using var bitmap = new Bitmap(1200, 110, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            using (var font = new Font("Arial", 24))
            { g.Clear(Color.FromArgb(25, 25, 25)); g.DrawString(sample, font, Brushes.White, 15, 20); }
            var watch = Stopwatch.StartNew();
            var text = await ocr.RecognizeAdaptiveAsync(bitmap, default, true);
            var firstOcrMs = watch.ElapsedMilliseconds;
            await Task.Delay(180); // The actual short confirmation interval, not a zero-time fixture.
            var second = await ocr.RecognizeAdaptiveAsync(bitmap, default, true);
            var stable = DialogueText.Identity(text) == DialogueText.Identity(second);
            var result = stable && !DialogueText.NeedsRepair(second)
                ? await service.TranslateAsync(second, false, config, default)
                : new TranslationResult(null, "辨識待確認", 0);
            passed &= result.Text is not null;
            rows.Add(new { expected = sample, ocr = text, second, stable, firstOcrMs,
                chinese = result.Text, source = result.Source, translationMs = result.ElapsedMs, totalMs = watch.ElapsedMilliseconds });
        }
        using var cancellationWorker = new LocalTranslationWorker();
        await cancellationWorker.TranslateAsync("The door remains closed.", 1, default);
        var originalProcess = cancellationWorker.ProcessId;
        var cancelled = false;
        using (var cancel = new CancellationTokenSource())
        {
            cancel.CancelAfter(1);
            try { await cancellationWorker.TranslateAsync("The skill is not ready yet. Wait for the cooldown.", 1, cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
        }
        var afterCancellation = Stopwatch.StartNew();
        var newest = await cancellationWorker.TranslateAsync("Leoric is waiting at the gate.", 2, default);
        var sameProcess = originalProcess == cancellationWorker.ProcessId;
        var cancellationPassed = cancelled && sameProcess && newest is not null && newest.Contains("Leoric");
        passed &= cancellationPassed;
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new {
            fixture = "Synthetic Arial text, includes 180ms confirmation; excludes game capture and initial polling wait",
            language = ocr.RecognizerLanguage, warmupMs = warm.ElapsedMs, rows,
            cancellation = new { cancelled, sameProcess, newest, nextMs = afterCancellation.ElapsedMilliseconds, passed = cancellationPassed }, passed
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }
}
