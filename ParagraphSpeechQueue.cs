namespace DiabloEnglishCoach;

// One whole paragraph owns playback until completion. Keep a small backlog;
// replacing stale pending entries must never stop the paragraph being spoken.
internal sealed class ParagraphSpeechQueue(Func<string, bool, Task<bool>> play)
{
    private sealed record Entry(string Text, bool Chinese, TaskCompletionSource<bool> Completion);
    private readonly object _gate = new();
    private readonly Queue<Entry> _pending = new();
    private Entry? _active;
    private CancellationTokenSource? _pumpCts;
    private bool _pumping;
    public bool IsBusy { get { lock (_gate) return _pumping; } }

    public Task<bool> Enqueue(string text, bool chinese)
    {
        var entry = new Entry(text, chinese, new(TaskCreationOptions.RunContinuationsAsynchronously));
        CancellationTokenSource? start = null;
        lock (_gate)
        {
            while (_pending.Count >= 3) _pending.Dequeue().Completion.TrySetResult(false);
            _pending.Enqueue(entry);
            if (!_pumping)
            {
                _pumping = true;
                start = _pumpCts = new CancellationTokenSource();
            }
        }
        if (start is not null) _ = PumpAsync(start);
        return entry.Completion.Task;
    }

    // Caller stops the actual player first; this also invalidates queued work.
    public void Clear()
    {
        lock (_gate)
        {
            var cancelledPump = _pumpCts;
            // Detach before cancellation: its continuation can run synchronously
            // and must not dequeue another entry from this (now stopped) pump.
            _pumpCts = null;
            _pumping = false;
            _active?.Completion.TrySetResult(false);
            _active = null;
            while (_pending.Count > 0) _pending.Dequeue().Completion.TrySetResult(false);
            cancelledPump?.Cancel();
        }
    }

    private async Task PumpAsync(CancellationTokenSource owner)
    {
        try
        {
            while (true)
            {
                Entry entry;
                Task<bool> playing;
                lock (_gate)
                {
                    if (!ReferenceEquals(_pumpCts, owner)) return;
                    if (_pending.Count == 0)
                    {
                        _pumping = false;
                        _active = null;
                        _pumpCts = null;
                        return;
                    }
                    _active = entry = _pending.Dequeue();
                    try { playing = play(entry.Text, entry.Chinese); }
                    catch { playing = Task.FromResult(false); }
                }
                bool completed;
                try { completed = await playing.WaitAsync(owner.Token); }
                catch { completed = false; }
                entry.Completion.TrySetResult(completed);
            }
        }
        finally { owner.Dispose(); }
    }
}
