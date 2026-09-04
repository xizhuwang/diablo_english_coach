using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record TranslationResult(string? Text, string Source, long ElapsedMs);

// Real-time translation is deliberately separate from Ollama. The API key comes
// from Windows Credential Manager and is never serialized into config/repository.
internal sealed class FastTranslationService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _path;
    private readonly Func<string?> _readKey;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _active;
    private int _cancelVersion;
    private DateTimeOffset _blockedUntil;
    public int CharactersUsed { get; private set; }

    public FastTranslationService(HttpClient? client = null, string? cachePath = null, Func<string?>? readKey = null)
    {
        _http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(8) };
        // Keep this provider cache separate from the retired MyMemory cache so
        // an old low-quality result can never be mistaken for an Azure result.
        _path = cachePath ?? Path.Combine(CoachConfig.FolderPath, "azure-translations.json");
        _readKey = readKey ?? AzureCredentialStore.Read;
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 1_000_000) return;
            var saved = JsonSerializer.Deserialize<SavedTranslations>(File.ReadAllText(_path));
            if (saved is null) return;
            foreach (var pair in saved.Cache.TakeLast(512))
                if (pair.Key.Length <= 700 && pair.Value.Length <= 1500 && !OcrService.IsChatOrAdvertising(pair.Key))
                    _cache[pair.Key] = pair.Value;
        }
        catch { /* A corrupt cache must never stop OCR. */ }
    }

    public async Task<TranslationResult> TranslateAsync(string text, bool quest, CoachConfig config, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        TranslationResult Result(string? value, string source) => new(value, source, watch.ElapsedMilliseconds);
        text = OcrService.Clean(text);
        if (!OcrService.LooksLikeEnglishSubtitle(text)) return Result(null, "已略過聊天／無關文字");
        if (TryLocal(text, quest) is { } local) return Result(local, "本機任務片語（名稱保留英文）");
        if (_cache.TryGetValue(text, out var cached)) return Result(cached, "本機翻譯快取");
        if (!config.OnlineTranslationEnabled) return Result(null, "線上翻譯已關閉");
        if (DateTimeOffset.UtcNow < _blockedUntil) return Result(null, "Azure 暫停重試中");
        var key = _readKey();
        if (string.IsNullOrWhiteSpace(key)) return Result(null, "請到設定輸入 Azure Translator F0 金鑰");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelVersion = _cancelVersion;
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        _active = timeout;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=en&to=zh-Hant");
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);
            if (!string.IsNullOrWhiteSpace(config.AzureTranslatorRegion))
                request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", config.AzureTranslatorRegion.Trim());
            request.Headers.TryAddWithoutValidation("X-ClientTraceId", Guid.NewGuid().ToString());
            request.Content = JsonContent.Create(new[] { new { Text = text } });
            CharactersUsed += text.Length;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result(null, "Azure 金鑰或區域不正確");
            if ((int)response.StatusCode == 429)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                _blockedUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(retry.TotalSeconds, 5, 3600));
                return Result(null, "Azure F0 流量限制，稍後自動重試");
            }
            if ((int)response.StatusCode >= 500)
            {
                _blockedUntil = DateTimeOffset.UtcNow.AddSeconds(15);
                return Result(null, "Azure 暫時無法服務");
            }
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(64_000, timeout.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var translated = json.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString()?.Trim() ?? "";
            if (translated.Length == 0 || translated.Length > 1500 || !translated.Any(c => c is >= '\u3400' and <= '\u9fff'))
                throw new InvalidDataException("Azure 沒有提供繁中譯文");
            _cache[text] = translated;
            while (_cache.Count > 512) _cache.Remove(_cache.Keys.First());
            await SaveAsync(timeout.Token);
            return Result(translated, "Azure Translator F0");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || cancelVersion != _cancelVersion) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or IOException or InvalidOperationException or KeyNotFoundException)
        {
            _blockedUntil = DateTimeOffset.UtcNow.AddSeconds(15);
            return Result(null, "Azure 連線失敗／逾時");
        }
        finally { if (ReferenceEquals(_active, timeout)) _active = null; }
    }

    internal static string? TryLocal(string text, bool quest)
    {
        var clean = OcrService.Clean(text).TrimEnd('.', '!', '?');
        if (clean.Equals("Follow me and stay close", StringComparison.OrdinalIgnoreCase)) return "跟著我，保持靠近。";
        if (clean.Equals("I need your help", StringComparison.OrdinalIgnoreCase)) return "我需要你的幫忙。";
        if (!quest) return null;
        var combined = Regex.Match(clean, @"^Head forward and search for ([A-Za-z][A-Za-z '\-]{0,80})$", RegexOptions.IgnoreCase);
        if (combined.Success) return $"往前走，尋找 {combined.Groups[1].Value}。";
        var match = Regex.Match(clean, @"\b(head to|talk to|search for|defeat|follow|find|enter|leave|return to)\s+([A-Za-z][A-Za-z '\-]{0,80})$", RegexOptions.IgnoreCase);
        if (!match.Success || Regex.IsMatch(match.Groups[2].Value, @"\b(and|or|then|before|after|until|when|if|to)\b", RegexOptions.IgnoreCase)) return null;
        var prefix = clean[..match.Index].Trim();
        if (prefix.Length > 0 && !prefix.EndsWith('.')) return null;
        if (Regex.IsMatch(prefix, @"\b(not|never|don't|do|if|must|should|can|cannot)\b", RegexOptions.IgnoreCase)) return null;
        var verb = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "head to" => "前往", "return to" => "返回", "talk to" => "交談對象：",
            "search for" or "find" => "尋找", "defeat" => "擊敗", "follow" => "跟隨",
            "enter" => "進入", "leave" => "離開", _ => ""
        };
        return $"{verb} {match.Groups[2].Value}。";
    }

    private async Task SaveAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new SavedTranslations(_cache));
        await File.WriteAllBytesAsync(_path + ".tmp", bytes, token);
        File.Move(_path + ".tmp", _path, true);
    }
    public void Cancel() { _cancelVersion++; _active?.Cancel(); }
    public void Dispose() { Cancel(); _http.Dispose(); }
    private sealed record SavedTranslations(Dictionary<string, string> Cache);
}
