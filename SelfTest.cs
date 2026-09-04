using System.Drawing.Imaging;
using System.Text.Json;
using EdgeTTS.DotNet;

namespace DiabloEnglishCoach;

internal static class SelfTest
{
    public static async Task<bool> TestQuestCoachAsync(string outputPath)
    {
        var checks = new Dictionary<string, object>();
        try
        {
            var reply = await new CoachService().GuideQuestAsync(
                "The Risen Dead. Head to Ashwold Cemetery.",
                string.Empty,
                new CoachConfig(),
                CancellationToken.None);
            checks["used_local_model"] = reply.UsedLocalModel;
            checks["simple_english"] = reply.SimpleEnglish;
            checks["traditional_chinese"] = reply.TraditionalChinese;
            checks["keywords"] = reply.Keywords.Select(item => new { item.Word, item.Meaning }).ToArray();

            var challenge = await new CoachService().AskAsync(
                "請用目前畫面的英文考我一題 A/B/C，不要公布答案。",
                "The Risen Dead. Head to Ashwold Cemetery.",
                string.Empty,
                Array.Empty<string>(),
                new CoachConfig(),
                CancellationToken.None);
            checks["challenge_used_local_model"] = challenge.UsedLocalModel;
            checks["challenge_simple_english"] = challenge.SimpleEnglish;
            checks["challenge_traditional_chinese"] = challenge.TraditionalChinese;

            var answer = await new CoachService().AskAsync(
                "A",
                "The Risen Dead. Head to Ashwold Cemetery.",
                string.Empty,
                new[]
                {
                    "PLAYER: 請考我一題",
                    $"COACH: {challenge.SimpleEnglish} / {challenge.TraditionalChinese}"
                },
                new CoachConfig(),
                CancellationToken.None);
            checks["answer_used_local_model"] = answer.UsedLocalModel;
            checks["answer_simple_english"] = answer.SimpleEnglish;
            checks["answer_traditional_chinese"] = answer.TraditionalChinese;

            var challengeText = challenge.SimpleEnglish + challenge.TraditionalChinese;
            var answerText = answer.SimpleEnglish + answer.TraditionalChinese;
            var passed = reply.UsedLocalModel &&
                         reply.TraditionalChinese.StartsWith("現在要做：", StringComparison.Ordinal) &&
                         reply.SimpleEnglish.Length > 0 &&
                         challenge.UsedLocalModel &&
                         challengeText.Contains('A') && challengeText.Contains('B') && challengeText.Contains('C') &&
                         answer.UsedLocalModel &&
                         (answerText.Contains("正確", StringComparison.OrdinalIgnoreCase) ||
                          answerText.Contains("correct", StringComparison.OrdinalIgnoreCase));
            checks["passed"] = passed;
            Write(outputPath, checks);
            return passed;
        }
        catch (Exception exception)
        {
            checks["passed"] = false;
            checks["error"] = exception.ToString();
            Write(outputPath, checks);
            return false;
        }
    }

    public static async Task<bool> TestScreenshotRegionsAsync(string imagePath, string outputPath)
    {
        var result = new Dictionary<string, object>();
        try
        {
            using var screenshot = new Bitmap(imagePath);
            var config = new CoachConfig();
            using var questCrop = CropNormalized(
                screenshot, config.QuestRegionX, config.QuestRegionY,
                config.QuestRegionWidth, config.QuestRegionHeight);
            using var questPrepared = CaptureService.PrepareForOcr(questCrop);
            var ocr = new OcrService();
            var questText = await ocr.RecognizeAsync(questPrepared, CancellationToken.None);
            var questFound = questText.Contains("Ashwold", StringComparison.OrdinalIgnoreCase) ||
                             questText.Contains("Cemetery", StringComparison.OrdinalIgnoreCase) ||
                             questText.Contains("Risen", StringComparison.OrdinalIgnoreCase);
            result["quest_text"] = questText;
            result["quest_objective_found"] = questFound;
            result["passed"] = questFound;
            Write(outputPath, result);
            return questFound;
        }
        catch (Exception exception)
        {
            result["passed"] = false;
            result["error"] = exception.ToString();
            Write(outputPath, result);
            return false;
        }
    }

    public static bool CaptureUiPreview(string outputPath)
    {
        try
        {
            // The preview process is terminated immediately by Program after the
            // bitmap is saved, so avoid WinForms/SAPI teardown influencing this
            // layout-only test.
            var form = new CoachForm(previewMode: true);
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            bitmap.Save(outputPath, ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> GenerateVoiceSampleAsync(string outputPath, string voice, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            var request = new Communicate(text, voice: voice, rate: "+18%", pitch: "+4Hz");
            await request.SaveAsync(outputPath);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> RunAsync(string outputPath)
    {
        var checks = new Dictionary<string, object>();
        try
        {
            using var source = new Bitmap(1200, 240, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            using (var font = new Font("Arial", 44))
            {
                graphics.Clear(Color.Black);
                graphics.DrawString("Follow Deckard Cain to Ashwold Cemetery", font, Brushes.White, 20, 40);
                graphics.DrawString("Defeat the undead and find the shard", font, Brushes.White, 20, 120);
            }

            using var prepared = CaptureService.PrepareForOcr(source);
            var ocr = new OcrService();
            var recognized = await ocr.RecognizeAsync(prepared, CancellationToken.None);
            checks["ocr_language"] = ocr.RecognizerLanguage;
            checks["ocr_text"] = recognized;
            checks["ocr_looks_english"] = OcrService.LooksLikeEnglishSubtitle(recognized);
            checks["ocr_key_name_found"] = recognized.Contains("Deckard", StringComparison.OrdinalIgnoreCase) ||
                                            recognized.Contains("Ashwold", StringComparison.OrdinalIgnoreCase);

            var config = new CoachConfig { OllamaUrl = "http://127.0.0.1:1", Model = "qwen3.5:2b-q4_K_M" };
            var fallback = await new CoachService().ExplainAsync(
                "Follow Deckard Cain to Ashwold Cemetery and defeat the undead.", config, CancellationToken.None);
            checks["fallback_works"] = !fallback.UsedLocalModel && fallback.Keywords.Count > 0;
            checks["fallback_keywords"] = fallback.Keywords.Select(item => item.Word).ToArray();

            var questFallback = await new CoachService().GuideQuestAsync(
                "The Risen Dead. Head to Ashwold Cemetery.", string.Empty, config, CancellationToken.None);
            checks["quest_fallback_works"] = !questFallback.UsedLocalModel &&
                                              questFallback.Original.StartsWith("QUEST ·", StringComparison.Ordinal) &&
                                              questFallback.Keywords.Any(item => item.Word == "cemetery");

            var parsed = CoachService.ParseModelReply(
                "Defeat the undead.",
                """{"simple_english":"Beat the undead.","traditional_chinese":"擊敗不死族。","keywords":[{"word":"defeat","meaning":"擊敗"},{"word":"future boss","meaning":"未來首領"}]}""");
            checks["model_json_parses"] = parsed.UsedLocalModel && parsed.TraditionalChinese == "擊敗不死族。";
            checks["unknown_keyword_filtered"] = parsed.Keywords.Count == 1 && parsed.Keywords[0].Word == "defeat";

            using var loadMonitor = new AdaptiveLoadMonitor();
            var sampledLoad = loadMonitor.Sample(coachBusy: false, explicitInteraction: false);
            checks["adaptive_load_sample_valid"] = sampledLoad.CpuPercent is >= 0 and <= 100 && sampledLoad.ActionKeyIdleMs >= 0;
            checks["adaptive_load_defers_input"] = AdaptiveLoadMonitor.ShouldDefer(20, 500, false, false);
            checks["adaptive_load_defers_high_cpu"] = AdaptiveLoadMonitor.ShouldDefer(85, 5000, false, false);
            checks["adaptive_load_resumes_when_idle"] = !AdaptiveLoadMonitor.ShouldDefer(30, 5000, false, false);
            checks["explicit_interaction_starts_immediately"] = !AdaptiveLoadMonitor.ShouldDefer(90, 100, true, true);
            checks["space_is_dialogue_not_action"] = !KeyboardActivityMonitor.IsActionKey(KeyboardActivityMonitor.SpaceVirtualKey);
            checks["wasd_is_action"] = KeyboardActivityMonitor.IsActionKey((int)Keys.W) &&
                                        KeyboardActivityMonitor.IsActionKey((int)Keys.A) &&
                                        KeyboardActivityMonitor.IsActionKey((int)Keys.S) &&
                                        KeyboardActivityMonitor.IsActionKey((int)Keys.D);

            var passed = checks.Values.OfType<bool>().All(value => value);
            checks["passed"] = passed;
            Write(outputPath, checks);
            return passed;
        }
        catch (Exception exception)
        {
            checks["passed"] = false;
            checks["error"] = exception.ToString();
            Write(outputPath, checks);
            return false;
        }
    }

    private static void Write(string outputPath, Dictionary<string, object> checks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Bitmap CropNormalized(Bitmap source, double x, double y, double width, double height)
    {
        var rectangle = new Rectangle(
            (int)Math.Round(source.Width * x),
            (int)Math.Round(source.Height * y),
            Math.Max(1, (int)Math.Round(source.Width * width)),
            Math.Max(1, (int)Math.Round(source.Height * height)));
        rectangle = Rectangle.Intersect(rectangle, new Rectangle(Point.Empty, source.Size));
        return source.Clone(rectangle, PixelFormat.Format32bppArgb);
    }
}
