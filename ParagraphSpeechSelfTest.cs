namespace DiabloEnglishCoach;

internal static class ParagraphSpeechSelfTest
{
    public static async Task<Dictionary<string, object>> RunAsync()
    {
        var checks = new Dictionary<string, object>();
        var starts = new List<string>();
        var finished = new Dictionary<string, TaskCompletionSource<bool>>();
        var gate = new object();
        var queue = new ParagraphSpeechQueue((text, _) =>
        {
            lock (gate)
            {
                starts.Add(text);
                finished[text] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return finished[text].Task;
            }
        });
        async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 200; i++)
            {
                if (condition()) return;
                await Task.Delay(5);
            }
            throw new TimeoutException("Speech queue regression timed out.");
        }
        void Finish(string text) { lock (gate) finished[text].TrySetResult(true); }
        var first = queue.Enqueue("first whole paragraph", true);
        var second = queue.Enqueue("second paragraph", true);
        checks["paragraph_new_text_does_not_interrupt"] = !first.IsCompleted && !second.IsCompleted && starts.Count == 1;
        checks["paragraph_busy_includes_playback"] = queue.IsBusy;
        Finish("first whole paragraph");
        checks["paragraph_first_completes_normally"] = await first;
        await Until(() => starts.Count == 2);
        checks["paragraph_fifo_order"] = starts.SequenceEqual(new[] { "first whole paragraph", "second paragraph" });
        Finish("second paragraph");
        checks["paragraph_second_completes_normally"] = await second;
        await Until(() => !queue.IsBusy);

        var active = queue.Enqueue("active", false);
        var pending = queue.Enqueue("pending", false);
        queue.Clear();
        checks["paragraph_explicit_stop_clears_all"] = !await active && !await pending && !queue.IsBusy;
        var fresh = queue.Enqueue("fresh", false);
        Finish("active"); // Late completion from stopped generation must not advance new pump.
        await Task.Delay(20);
        checks["paragraph_stale_completion_cannot_restart"] = !starts.Contains("pending") && !fresh.IsCompleted;
        Finish("fresh");
        checks["paragraph_restart_after_stop"] = await fresh;
        await Until(() => !queue.IsBusy);

        var longOne = queue.Enqueue("long paragraph", false);
        var stale = queue.Enqueue("stale pending", false);
        var b = queue.Enqueue("b", false);
        var c = queue.Enqueue("c", false);
        var d = queue.Enqueue("d", false);
        checks["paragraph_backlog_bounded_not_active"] = !await stale && !longOne.IsCompleted && !starts.Contains("b");
        queue.Clear();
        Finish("long paragraph");
        await Task.WhenAll(longOne, b, c, d);

        var recover = new ParagraphSpeechQueue((text, _) => text == "broken"
            ? Task.FromException<bool>(new IOException("fake player failure")) : Task.FromResult(true));
        checks["paragraph_failure_does_not_block_next"] = !await recover.Enqueue("broken", false) &&
            await recover.Enqueue("next", false);
        return checks;
    }
}
