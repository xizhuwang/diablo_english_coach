using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record SpeakingPrompt(string English, string Chinese);

internal static class SpeakingSession
{
    // Kept independent of UI/hardware so cancellation and mic timing can be tested.
    public static async Task<string> RunAsync(Func<Task<bool>> play,
        Func<CancellationToken, Task<SpeakingRecording>> record,
        Func<byte[], CancellationToken, Task<string>> decode,
        Action<bool> microphoneStage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var played = await play().WaitAsync(TimeSpan.FromSeconds(20), token);
        token.ThrowIfCancellationRequested();
        if (!played) throw new InvalidOperationException("示範聲音未完成，已略過收音。");
        await Task.Delay(350, token);
        token.ThrowIfCancellationRequested();
        microphoneStage(true);
        token.ThrowIfCancellationRequested();
        var recording = await record(token);
        try
        {
            token.ThrowIfCancellationRequested();
            microphoneStage(false);
            var text = recording.HeardSound ? await decode(recording.Pcm, token) : "";
            token.ThrowIfCancellationRequested();
            return text;
        }
        finally { Array.Clear(recording.Pcm); }
    }
}

// Pure clock-driven policy. No microphone, game input or model work here.
internal sealed class SpeakingPlanner
{
    internal const long IntervalMs = 240_000;
    internal const long QuietMs = 15_000;
    private long _nextDue;
    private long? _quietSince;
    private int _index;
    private static readonly SpeakingPrompt[] Prompts =
    [
        new("Where should I go next?", "我接下來該去哪裡？"),
        new("I need your help.", "我需要你的幫忙。"),
        new("Which item should I choose?", "我應該選哪件物品？"),
        new("Let me check the map.", "讓我查看地圖。"),
        new("I need more time.", "我需要更多時間。"),
        new("This skill deals more damage.", "這個技能造成更多傷害。"),
        new("Stay close to me.", "待在我附近。"),
        new("I am ready to help.", "我準備好幫忙了。")
    ];

    public void Reset(long now)
    {
        _nextDue = now + IntervalMs;
        _quietSince = null;
    }

    public SpeakingPrompt? TryNext(long now, bool enabled, bool safe)
    {
        if (!enabled || !safe)
        {
            _quietSince = null;
            return null;
        }
        _quietSince ??= now;
        if (now < _nextDue || now - _quietSince.Value < QuietMs)
            return null;
        Reset(now); // Interrupted/skipped exercises never accumulate.
        return Prompts[_index++ % Prompts.Length];
    }

    internal static bool CanStart(bool enabled, bool foreground, bool pendingWork,
        double cpu, int actionIdleMs, int dialogueKeyIdleMs, long sinceDialogueMs) =>
        enabled && foreground && !pendingWork && cpu < 55 && actionIdleMs >= QuietMs &&
        dialogueKeyIdleMs >= QuietMs && sinceDialogueMs >= QuietMs;

    internal static bool ShouldInterrupt(bool running, bool foreground, bool settingsOpen,
        double cpu, int actionIdleMs, int dialogueKeyIdleMs) =>
        !running || !foreground || settingsOpen || cpu >= 72 ||
        actionIdleMs < 2500 || dialogueKeyIdleMs < 2500;
}

internal static class SpeakingFeedback
{
    private static string[] Words(string value) => Regex.Matches(value.ToLowerInvariant(), "[a-z]+(?:'[a-z]+)?")
        .Select(match => match.Value).Take(40).ToArray();

    public static string Describe(string target, string recognized)
    {
        var expected = Words(target);
        var heard = Words(recognized);
        if (heard.Length == 0)
            return "這次沒有辨識到清楚的英文，先繼續玩；下次再試。";
        if (expected.SequenceEqual(heard))
            return "辨識文字與練習句一致！這是文字核對，不代表發音評分。";
        // Ordered matching (LCS): repeated words and swapped word order matter.
        var table = new int[expected.Length + 1, heard.Length + 1];
        for (var i = expected.Length - 1; i >= 0; i--)
            for (var j = heard.Length - 1; j >= 0; j--)
                table[i, j] = expected[i] == heard[j] ? 1 + table[i + 1, j + 1]
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
        var missing = new List<string>();
        for (int i = 0, j = 0; i < expected.Length;)
        {
            if (j < heard.Length && expected[i] == heard[j]) { i++; j++; }
            else if (j < heard.Length && table[i, j + 1] > table[i + 1, j]) j++;
            else missing.Add(expected[i++]);
        }
        return missing.Count == 0 ? "有聽到目標句的用字，也辨識到其他字；可再跟讀一次短句。"
            : $"可能沒聽清：{string.Join(" · ", missing.Take(5))}。下次留意這幾個字；也可能是辨識誤差。";
    }
}
