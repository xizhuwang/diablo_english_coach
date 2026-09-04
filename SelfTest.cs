using System.Drawing.Imaging;
using System.Text.Json;

namespace DiabloEnglishCoach;

internal static class SelfTest
{
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

            var parsed = CoachService.ParseModelReply(
                "Defeat the undead.",
                """{"simple_english":"Beat the undead.","traditional_chinese":"擊敗不死族。","keywords":[{"word":"defeat","meaning":"擊敗"},{"word":"future boss","meaning":"未來首領"}]}""");
            checks["model_json_parses"] = parsed.UsedLocalModel && parsed.TraditionalChinese == "擊敗不死族。";
            checks["unknown_keyword_filtered"] = parsed.Keywords.Count == 1 && parsed.Keywords[0].Word == "defeat";

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
}
