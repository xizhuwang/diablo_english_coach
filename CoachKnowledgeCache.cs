using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiabloEnglishCoach;

// Persists only the small model's finished teaching decision. Source screenshots,
// audio, API keys and raw OCR context are never written here. A prompt-versioned
// hash prevents stale prompt behavior from being reused after future changes.
internal sealed class CoachKnowledgeCache
{
    private const string PromptVersion = "coach-v2-direct-speech";
    private const int Limit = 128;
    private readonly string _path;
    private readonly Dictionary<string, SavedReply> _items = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public CoachKnowledgeCache(string? path = null)
    {
        _path = path ?? Path.Combine(CoachConfig.FolderPath, "coach-knowledge.json");
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 512_000) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, SavedReply>>(File.ReadAllText(_path));
            if (saved is null) return;
            foreach (var pair in saved.TakeLast(Limit))
                if (Valid(pair.Value)) _items[pair.Key] = pair.Value;
        }
        catch { /* A damaged optional cache must never stop the coach. */ }
    }

    public bool TryGet(string kind, string context, string displayOriginal, out CoachReply reply)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(Key(kind, context), out var saved) && Valid(saved))
            {
                reply = new CoachReply(displayOriginal, saved.SimpleEnglish, saved.TraditionalChinese,
                    saved.Keywords, true, "本機教練快取");
                return true;
            }
        }
        reply = null!;
        return false;
    }

    public void Store(string kind, string context, CoachReply reply)
    {
        if (!reply.UsedLocalModel) return;
        lock (_gate)
        {
            _items[Key(kind, context)] = new SavedReply(reply.SimpleEnglish,
                reply.TraditionalChinese, reply.Keywords.Take(3).ToArray());
            while (_items.Count > Limit) _items.Remove(_items.Keys.First());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_items));
                File.Move(temporary, _path, true);
            }
            catch { /* Cache persistence is optional. */ }
        }
    }

    private static string Key(string kind, string context)
    {
        var normalized = string.Join(' ', context.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{PromptVersion}|{kind}|{normalized}")));
    }

    private static bool Valid(SavedReply value) =>
        value.SimpleEnglish is { Length: > 0 and <= 200 } &&
        value.TraditionalChinese is { Length: > 0 and <= 600 } &&
        value.Keywords is { Length: <= 3 } && value.Keywords.All(item =>
            item.Word is { Length: > 0 and <= 40 } && item.Meaning is { Length: > 0 and <= 120 });

    private sealed record SavedReply(string SimpleEnglish, string TraditionalChinese, KeywordCard[] Keywords);
}
