using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record SpeakingPrompt(string English, string Chinese, string Knowledge = "把句子分成兩個短片語，通常比較容易說順。");

internal static class SpeakingSession
{
    // RecordAsync has already closed the microphone before this function runs.
    // Never delay decoding to fill time, and never start a bridge for a fast result.
    internal static async Task<string> DecodeWithBridgeAsync(Func<Task<string>> decode,
        Func<Task<bool>> bridge, CancellationToken token, int delayMs = 1200)
    {
        token.ThrowIfCancellationRequested();
        var decoding = decode();
        Task<bool>? playing = null;
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            await Task.WhenAny(decoding, Task.Delay(delayMs, delayCts.Token));
            if (!decoding.IsCompleted && !token.IsCancellationRequested)
            {
                try { playing = bridge(); }
                catch { /* A synchronous playback failure also leaves decoding intact. */ }
            }
        }
        finally { delayCts.Cancel(); }
        // Await the decoder itself even on cancellation: native decoding must
        // finish releasing the PCM buffer before SpeakingSession clears it.
        try { return await decoding; }
        finally
        {
            if (playing is not null)
            {
                try { await playing; } // One started paragraph finishes before feedback.
                catch { /* Optional bridge audio must not discard a valid recognition. */ }
            }
        }
    }

    // Kept independent of UI/hardware so cancellation and mic timing can be tested.
    public static async Task<string> RunAsync(Func<Task<bool>> play,
        Func<CancellationToken, Task<SpeakingRecording>> record,
        Func<byte[], CancellationToken, Task<string>> decode,
        Action<bool> microphoneStage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var played = await play().WaitAsync(TimeSpan.FromSeconds(40), token);
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
    internal const long IntervalMs = 90_000;
    internal const long QuietMs = 15_000;
    private long _nextDue;
    private long? _quietSince;
    private int _index;
    private static readonly SpeakingPrompt[] Prompts =
    [
        new("Where should I go next?", "我接下來該去哪裡？", "Where should I go 是詢問該去哪裡；next 表示接下來。"),
        new("I need your help.", "我需要你的幫忙。", "need 後面接需要的東西；your help 就是你的幫忙。"),
        new("Which item should I choose?", "我應該選哪件物品？", "which 用來問哪一個；should I choose 是我該選。"),
        new("Let me check the map.", "讓我查看地圖。", "Let me 後面接原形動詞；check 是查看。"),
        new("I need more time.", "我需要更多時間。", "more time 是更多時間；time 在這裡不用加複數。"),
        new("This skill deals more damage.", "這個技能造成更多傷害。", "deal damage 是造成傷害；單數 skill 搭配 deals。"),
        new("Stay close to me.", "待在我附近。", "stay close to 是保持靠近，後面可接人或地點。"),
        new("I am ready to help.", "我準備好幫忙了。", "ready to 後面接原形動詞，表示準備好做某件事。"),
        new("Please confirm the schedule.", "請確認時程。", "confirm 後面接要確認的事情；schedule 是時程。"),
        new("The meeting has been postponed.", "會議已經延期。", "has been postponed 表示已被延期。"),
        new("Submit the form before the deadline.", "在截止期限前提交表單。", "before 表示在某個時間之前；deadline 是截止期限。"),
        new("A flip-flop stores one bit.", "正反器儲存一個位元。", "stores 是儲存；one bit 是一個位元。"),
        new("Reset places the design in a known state.", "重置會把設計帶到已知狀態。", "known state 是已知狀態；reset 是重置。"),
        new("Combinational logic depends on current inputs.", "組合邏輯取決於目前輸入。", "depends on 表示取決於；inputs 是輸入。")
    ];

    public void Reset(long now)
    {
        _nextDue = now + IntervalMs;
        _quietSince = null;
    }

    public SpeakingPrompt? TryNext(long now, bool enabled, bool safe, bool readyToInvite = true,
        SpeakingPrompt? recentLesson = null)
    {
        if (!enabled || !safe)
        {
            _quietSince = null;
            return null;
        }
        _quietSince ??= now;
        if (now < _nextDue || now - _quietSince.Value < QuietMs || !readyToInvite)
            return null;
        Reset(now); // Interrupted/skipped exercises never accumulate.
        var fallback = Prompts[_index++ % Prompts.Length];
        return recentLesson ?? fallback;
    }

    internal static SpeakingPrompt? FromLesson(IdleLesson? lesson)
    {
        if (lesson is null || lesson.English.Length > 100 ||
            Regex.Matches(lesson.English, @"[A-Za-z]+(?:[-'][A-Za-z]+)*").Count is < 3 or > 14 ||
            string.IsNullOrWhiteSpace(lesson.SentenceMeaning)) return null;
        // Keep the bridge short, on-topic, and already available without an LLM.
        var knowledge = lesson.Chinese.Split('。', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (knowledge.Length is < 3 or > 65)
            knowledge = lesson.SentenceMeaning.Length <= 50
                ? $"整句意思是：{lesson.SentenceMeaning}"
                : "這次先練習把整句說順，再留意句尾的字。";
        return new(lesson.English, lesson.SentenceMeaning, knowledge);
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
