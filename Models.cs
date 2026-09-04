using System.Text.Json;

namespace DiabloEnglishCoach;

internal sealed class CoachConfig
{
    public double RegionX { get; set; } = 0.12;
    public double RegionY { get; set; } = 0.58;
    public double RegionWidth { get; set; } = 0.76;
    public double RegionHeight { get; set; } = 0.30;
    public int ScanIntervalMs { get; set; } = 1100;
    public string OllamaUrl { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "qwen3.5:2b-q4_K_M";
    public bool SpeakChinese { get; set; } = true;
    public bool SpeakEnglish { get; set; }
    public bool ShowOriginal { get; set; } = true;
    public bool ForceCpuInference { get; set; } = true;

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

internal sealed record KeywordCard(string Word, string Meaning);

internal sealed record CoachReply(
    string Original,
    string SimpleEnglish,
    string TraditionalChinese,
    IReadOnlyList<KeywordCard> Keywords,
    bool UsedLocalModel,
    string? Notice = null);

internal sealed record WindowInfo(nint Handle, string Title, Rectangle ClientBounds)
{
    public override string ToString() => Title;
}
