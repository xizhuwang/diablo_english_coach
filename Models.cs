using System.Text.Json;

namespace DiabloEnglishCoach;

internal sealed class CoachConfig
{
    public double RegionX { get; set; } = 0.27;
    public double RegionY { get; set; } = 0.64;
    public double RegionWidth { get; set; } = 0.46;
    public double RegionHeight { get; set; } = 0.24;
    public bool OnlineTranslationEnabled { get; set; }
    public string TranslationProvider { get; set; } = TranslationProviders.LocalOllama;
    public string TranslationModel { get; set; } = "qwen3.5:0.8b";
    public string AzureTranslatorRegion { get; set; } = "";
    public int OverlayLayoutVersion { get; set; }
    public double QuestRegionX { get; set; } = 0.005;
    public double QuestRegionY { get; set; } = 0.20;
    public double QuestRegionWidth { get; set; } = 0.22;
    public double QuestRegionHeight { get; set; } = 0.23;
    public bool QuestRegionConfigured { get; set; } = true;
    public int ScanIntervalMs { get; set; } = 1100;
    public int BusyScanIntervalMs { get; set; } = 2800;
    public string OllamaUrl { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "qwen3.5:2b-q4_K_M";
    public bool SpeakChinese { get; set; } = true;
    public bool SpeakEnglish { get; set; }
    public bool ShowOriginal { get; set; } = true;
    public bool ShowCoachTranscript { get; set; } = true;
    public bool ForceCpuInference { get; set; } = true;
    public int InferenceThreads { get; set; } = 2;
    public bool UseOnlineNeuralVoice { get; set; } = true;
    public string ChineseVoice { get; set; } = "zh-TW-HsiaoYuNeural";
    public string EnglishVoice { get; set; } = "en-US-AnaNeural";
    public string LocalChineseVoice { get; set; } = "Microsoft Yating Desktop";
    public string LocalEnglishVoice { get; set; } = "Microsoft Zira Desktop";
    public int SpeechRatePercent { get; set; } = 0;
    public int ExperienceVersion { get; set; }
    public int TeachingPauseSeconds { get; set; } = 6;
    public int SpeakingIntervalSeconds { get; set; } = 60;
    public int SpeechPitchHz { get; set; } = 4;
    public double WindowOpacity { get; set; } = 0.80;
    public int WindowLeft { get; set; } = -1;
    public int WindowTop { get; set; } = -1;
    public int WindowBottom { get; set; } = -1;
    public int TranslationWidth { get; set; }
    public bool CompactMode { get; set; }
    public bool BuildTipsEnabled { get; set; } = true;
    // Opt-in only: upgrading or starting the coach never grants microphone consent.
    public bool AutoSpeakingEnabled { get; set; }
    public string LearningFocus { get; set; } = LearningFocusOptions.Balanced;
    public string PlayerClass { get; set; } = "Necromancer";
    public string BuildPreference { get; set; } = "EasyPvE";

    public static string FolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiabloEnglishCoach");

    public static string FilePath => Path.Combine(FolderPath, "config.json");

    public static CoachConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<CoachConfig>(File.ReadAllText(FilePath)) ?? new CoachConfig();
        }
        catch
        {
            // A broken preference file should never prevent the coach from opening.
        }

        return new CoachConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class LearningFocusOptions
{
    public const string Balanced = "Balanced";
    public const string ToeicFirst = "ToeicFirst";
    public const string DigitalIcFirst = "DigitalIcFirst";
}

internal static class TranslationProviders
{
    public const string LocalOllama = "LocalOllama";
    public const string Azure = "Azure";
    public const string Disabled = "Disabled";
}

internal sealed record KeywordCard(string Word, string Meaning);

internal sealed record CoachReply(
    string Original,
    string SimpleEnglish,
    string TraditionalChinese,
    IReadOnlyList<KeywordCard> Keywords,
    bool UsedLocalModel,
    string? Notice = null,
    string? Advice = null);

internal sealed record WindowInfo(nint Handle, string Title, Rectangle ClientBounds)
{
    public override string ToString() => Title;
}

internal enum CaptureRegionKind
{
    Dialogue,
    Quest
}
