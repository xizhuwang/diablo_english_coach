using System.Net;
using System.Text.Json;

namespace DiabloEnglishCoach;

internal static class BuildGuideSelfTest
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    internal static async Task<Dictionary<string, object>> RunAsync(string? html = null, string? cachePath = null)
    {
        html ??= "<time datetime='2026-06-17T13:59:05Z'>Updated</time>" +
            "<h2>Best Necromancer Diablo Immortal build</h2><table>" +
            string.Join("", new[] {
                ("Primary attack", "Soulfire (Ultimate: ignored)"), ("Skill 1", "Command Skeletons"),
                ("Skill 2", "Corpse Explosion"), ("Skill 3", "Bone Armor"), ("Skill 4", "Command Golem"),
                ("Armor", "Shepherd’s Call to Wolves set"), ("Main weapon", "Desolatoria"), ("Off-hand weapon", "Life in Balance")
            }.Select(pair => $"<tr><td>{pair.Item1}</td><td><strong>{pair.Item2}</strong></td></tr>")) + "</table><h2>PvP</h2>";
        cachePath ??= Path.Combine(Path.GetTempPath(), "diablo-guide-test-" + Guid.NewGuid().ToString("N"), "guide.json");
        var checks = new Dictionary<string, object>();
        var parsed = BuildGuideCache.Parse(html, DateTimeOffset.UtcNow);
        checks["guide_extracts_pve_only"] = parsed.Skills.Length == 4 && parsed.Skills.Contains("Command Skeletons");
        checks["guide_ignores_ultimate_claim"] = parsed.PrimaryAttack == "Soulfire";
        checks["guide_keeps_author_date"] = parsed.SourceUpdatedAt.Year == 2026 && parsed.SourceUpdatedAt.Month == 6;
        checks["guide_checked_is_not_updated"] = parsed.CheckedAt > parsed.SourceUpdatedAt;
        checks["guide_marks_old_source"] = BuildGuideCache.IsStale(parsed, new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));
        checks["guide_rejects_wrong_source"] = !BuildGuideCache.Valid(parsed with { SourceUrl = "https://example.com/" }, DateTimeOffset.UtcNow);
        checks["guide_rejects_future_date"] = !BuildGuideCache.Valid(parsed with { SourceUpdatedAt = DateTimeOffset.UtcNow.AddDays(1) }, DateTimeOffset.UtcNow);
        try { BuildGuideCache.Parse("<h1>Verify you are human</h1>", DateTimeOffset.UtcNow); checks["guide_rejects_challenge"] = false; }
        catch (InvalidDataException) { checks["guide_rejects_challenge"] = true; }
        try { BuildGuideCache.Parse(html.Replace("Command Golem", "Command Skeletons"), DateTimeOffset.UtcNow); checks["guide_rejects_duplicate_skills"] = false; }
        catch (InvalidDataException) { checks["guide_rejects_duplicate_skills"] = true; }

        var cache = new BuildGuideCache(cachePath);
        using var good = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test-guide\"");
            return response;
        }));
        await cache.RefreshAsync(CancellationToken.None, good);
        checks["guide_saves_valid_update"] = File.Exists(cachePath) && cache.Current.ContentHash == parsed.ContentHash;
        var saved = cache.Current;
        using var notModified = new HttpClient(new Handler(request =>
        {
            checks["guide_uses_conditional_request"] = request.Headers.IfNoneMatch.Any();
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }));
        await cache.RefreshAsync(CancellationToken.None, notModified);
        checks["guide_304_preserves_author_date"] = cache.Current.SourceUpdatedAt == saved.SourceUpdatedAt;
        using var bad = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var beforeFailure = cache.Current;
        await cache.RefreshAsync(CancellationToken.None, bad);
        checks["guide_network_failure_keeps_cache"] = cache.Current == beforeFailure && cache.LastResult.StartsWith("更新失敗");
        using var invalid = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("captcha") }));
        await cache.RefreshAsync(CancellationToken.None, invalid);
        checks["guide_bad_html_keeps_cache"] = cache.Current == beforeFailure;
        File.WriteAllText(cachePath, "broken-json");
        checks["guide_corruption_recovers_backup"] = new BuildGuideCache(cachePath).Current.ContentHash == saved.ContentHash;
        checks["guide_unsupported_class_no_advice"] = cache.Lesson(new CoachConfig { PlayerClass = "Wizard" }, 0) is null;
        checks["guide_disabled_no_advice"] = cache.Lesson(new CoachConfig { BuildTipsEnabled = false }, 0) is null;
        checks["guide_no_owned_equipment_claim"] = cache.Lesson(new CoachConfig(), 2)!.Chinese.Contains("不代表你已持有");
        checks["guide_no_latest_claim"] = cache.Lesson(new CoachConfig(), 0)!.Title.Contains("非最新保證");
        return checks;
    }

    public static async Task<bool> FileTestAsync(string htmlPath, string output)
    {
        Dictionary<string, object> result;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(output)!, "guide-test-cache", Guid.NewGuid().ToString("N"), "guide.json");
            result = await RunAsync(await File.ReadAllTextAsync(htmlPath), path);
            result["passed"] = result.Values.OfType<bool>().All(value => value);
        }
        catch (Exception ex) { result = new() { ["passed"] = false, ["error"] = ex.ToString() }; }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result["passed"] is true;
    }

    public static async Task<bool> LiveTestAsync(string output)
    {
        var path = Path.Combine(Path.GetDirectoryName(output)!, "guide-test-cache", "live-" + Guid.NewGuid().ToString("N"), "guide.json");
        var cache = new BuildGuideCache(path);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await cache.RefreshAsync(CancellationToken.None);
        var result = new
        {
            passed = cache.Current.CheckedAt is not null && !cache.LastResult.StartsWith("更新失敗"),
            elapsedMs = timer.ElapsedMilliseconds,
            cache.Status,
            cache.LastResult,
            guide = cache.Current,
            microphoneUsed = false,
            llmUsed = false
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result.passed;
    }
}
