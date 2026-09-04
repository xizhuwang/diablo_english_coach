using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record BuildGuideSnapshot(
    string SourceUrl, DateTimeOffset SourceUpdatedAt, DateTimeOffset? CheckedAt,
    string PrimaryAttack, string[] Skills, string SetName, string MainWeapon, string OffHand,
    string ContentHash, string? ETag = null);

// Read-only public source -> tightly scoped facts -> atomic local cache.
// No LLM, browser, game text upload, rankings, future story, or full article mirror.
internal sealed class BuildGuideCache
{
    internal const string SourceUrl = "https://www.dexerto.com/diablo/best-diablo-immortal-necromancer-builds-1870129/";
    internal const int MaxHtmlBytes = 1_500_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private BuildGuideSnapshot _current;
    private string _lastResult = "尚未進行本次啟動檢查";
    private int _refreshing;

    public BuildGuideSnapshot Current => Volatile.Read(ref _current);
    public string LastResult => Volatile.Read(ref _lastResult);
    public BuildGuideCache(string? path = null)
    {
        _path = path ?? Path.Combine(CoachConfig.FolderPath, "guides", "necromancer-pve.json");
        _current = LoadOrSeed(_path);
    }

    // Manually inspected 2026-09-04. Author date is deliberately NOT replaced by
    // our inspection date. This source predates later balancing: never label BiS.
    internal static BuildGuideSnapshot Seed => new(SourceUrl,
        DateTimeOffset.Parse("2026-06-17T13:59:05Z", CultureInfo.InvariantCulture), null,
        "Soulfire", ["Command Skeletons", "Corpse Explosion", "Bone Armor", "Command Golem"],
        "Shepherd’s Call to Wolves set", "Desolatoria", "Life in Balance", "bundled-reference");

    public string Status => $"死靈 PvE 參考 · 來源 {Current.SourceUpdatedAt:yyyy-MM-dd} · " +
        (Current.CheckedAt is { } time ? $"查詢 {time.ToLocalTime():MM-dd HH:mm}" : "內建快取") +
        (IsStale(Current, DateTimeOffset.UtcNow) ? " · 來源較舊，非當季最強保證" : " · 非當季最強保證");

    internal static bool IsStale(BuildGuideSnapshot guide, DateTimeOffset now) => now - guide.SourceUpdatedAt > TimeSpan.FromDays(30);

    public async Task RefreshAsync(CancellationToken cancellationToken, HttpClient? testClient = null)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var ownedClient = testClient is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : null;
        var client = testClient ?? ownedClient!;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
            request.Headers.UserAgent.ParseAdd("DiabloEnglishCoach/1.0");
            var previous = Current;
            if (!string.IsNullOrWhiteSpace(previous.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", previous.ETag);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            BuildGuideSnapshot candidate;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (string.IsNullOrWhiteSpace(previous.ETag)) throw new InvalidDataException("無法驗證未變更回應");
                candidate = previous with { CheckedAt = DateTimeOffset.UtcNow };
            }
            else
            {
                response.EnsureSuccessStatusCode(); // Redirects/challenges are not followed or bypassed.
                if (response.Content.Headers.ContentLength > MaxHtmlBytes) throw new InvalidDataException("來源頁面超出大小限制");
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (bytes.Length + count > MaxHtmlBytes) throw new InvalidDataException("來源頁面超出大小限制");
                    bytes.Write(buffer, 0, count);
                }
                var html = Encoding.UTF8.GetString(bytes.ToArray());
                candidate = await Task.Run(() => Parse(html, DateTimeOffset.UtcNow), timeout.Token);
                candidate = candidate with { ETag = response.Headers.ETag?.ToString() };
            }
            timeout.Token.ThrowIfCancellationRequested();
            if (candidate.SourceUpdatedAt < previous.SourceUpdatedAt) throw new InvalidDataException("來源日期倒退，沿用較新的本機資料");
            // Do not overwrite the last usable file until parsing/validation succeeded.
            await SaveAsync(candidate, timeout.Token);
            Volatile.Write(ref _current, candidate);
            Volatile.Write(ref _lastResult, previous.ContentHash == candidate.ContentHash
                ? "來源未變更；已更新檢查時間（不是攻略更新日期）" : "已更新本機參考欄位；尚未驗證當季最強流派");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastResult, $"更新失敗，沿用原快取：{ex.Message}");
        }
        finally { Volatile.Write(ref _refreshing, 0); }
    }

    private async Task SaveAsync(BuildGuideSnapshot guide, CancellationToken token)
    {
        var folder = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(folder);
        var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(guide, JsonOptions), token);
            token.ThrowIfCancellationRequested();
            if (File.Exists(_path)) File.Copy(_path, _path + ".previous", overwrite: true);
            File.Move(temp, _path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static BuildGuideSnapshot LoadOrSeed(string path)
    {
        foreach (var candidate in new[] { path, path + ".previous" })
        {
            try
            {
                if (!File.Exists(candidate) || new FileInfo(candidate).Length > 16_384) continue;
                var guide = JsonSerializer.Deserialize<BuildGuideSnapshot>(File.ReadAllText(candidate));
                if (guide is not null && Valid(guide, DateTimeOffset.UtcNow)) return guide;
            }
            catch { /* A corrupt cache must not prevent startup. */ }
        }
        return Seed;
    }

    internal static BuildGuideSnapshot Parse(string html, DateTimeOffset now)
    {
        if (html.Length > MaxHtmlBytes) throw new InvalidDataException("來源過大");
        var dateMatch = Match(html, "<time\\b[^>]*datetime=[\"'](?<date>[^\"']+)[\"']");
        if (!DateTimeOffset.TryParse(dateMatch.Groups["date"].Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date)) throw new InvalidDataException("缺少來源更新日期");
        var sections = Regex.Matches(html, "<h2\\b[^>]*>(?<heading>.*?)</h2>(?<body>.*?)(?=<h2\\b|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        string? body = null;
        foreach (Match section in sections)
            if (Plain(section.Groups["heading"].Value) == "Best Necromancer Diablo Immortal build" &&
                section.Groups["body"].Value.Contains("<table", StringComparison.OrdinalIgnoreCase))
                body = Match(section.Groups["body"].Value, "<table\\b[^>]*>(?<table>.*?)</table>").Groups["table"].Value;
        if (string.IsNullOrWhiteSpace(body)) throw new InvalidDataException("來源版面改變，找不到 PvE 配置表");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in Regex.Matches(body, "<tr\\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var cells = Regex.Matches(row.Value, "<td\\b[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (cells.Count == 2) fields[Plain(cells[0].Groups[1].Value)] = Plain(cells[1].Groups[1].Value);
        }
        string Field(string key) => fields.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"缺少欄位：{key}");
        var primary = Field("Primary attack").Split('(')[0].Trim(); // Don't copy Ultimate claims.
        var skills = Enumerable.Range(1, 4).Select(i => Field($"Skill {i}")).ToArray();
        var names = string.Join("|", new[] { primary }.Concat(skills).Concat(new[] { Field("Armor"), Field("Main weapon"), Field("Off-hand weapon") }));
        var parsed = new BuildGuideSnapshot(SourceUrl, date, now, primary, skills, Field("Armor"), Field("Main weapon"), Field("Off-hand weapon"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(date.ToString("O") + names))));
        if (!Valid(parsed, now)) throw new InvalidDataException("攻略欄位或日期不合理，未套用");
        return parsed;
    }

    private static Match Match(string input, string pattern) => Regex.Match(input, pattern,
        RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private static string Plain(string html) => Regex.Replace(WebUtility.HtmlDecode(
        Regex.Replace(html, "<[^>]*>", " ", RegexOptions.Singleline, TimeSpan.FromSeconds(1))), "\\s+", " ").Trim();
    internal static bool Valid(BuildGuideSnapshot value, DateTimeOffset now) =>
        value.SourceUrl == SourceUrl && value.SourceUpdatedAt.Year >= 2022 && value.SourceUpdatedAt <= now &&
        (value.CheckedAt is null || value.CheckedAt <= now.AddMinutes(5)) &&
        value.Skills is { Length: 4 } && value.Skills.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4 &&
        new[] { value.PrimaryAttack, value.SetName, value.MainWeapon, value.OffHand }.Concat(value.Skills)
            .All(name => name is { Length: > 1 and <= 90 } && Regex.IsMatch(name, "^[A-Za-z0-9 ’'(),&+\\-]+$")) &&
        !string.IsNullOrWhiteSpace(value.ContentHash);

    public IdleLesson? Lesson(CoachConfig config, int index)
    {
        if (!config.BuildTipsEnabled || config.PlayerClass != "Necromancer") return null;
        var guide = Current;
        var heading = $"本機攻略 · 來源 {guide.SourceUpdatedAt:MM-dd} · 非最新保證";
        // Conditional references, not an assertion of owned/unlocked equipment.
        var cards = new[]
        {
            new IdleLesson(heading, guide.Skills[0], "PvE 來源列出此技能；若已解鎖，可先讀技能效果。skill＝技能；effect＝效果。"),
            new IdleLesson(heading, guide.SetName, "來源列出的套裝候選，非前期必需。set＝套裝；piece＝件數。實際效果以遊戲為準。"),
            new IdleLesson(heading, guide.MainWeapon, "來源列出的武器候選，不代表你已持有或應立即更換。先比較目前技能與傳奇效果。"),
            new IdleLesson(heading, "Compare skills before changing gear.", $"比較技能後再換裝；來源技能之一：{guide.Skills[2]}。未解鎖就先保留現有配置。")
        };
        return cards[(index & int.MaxValue) % cards.Length];
    }
}
