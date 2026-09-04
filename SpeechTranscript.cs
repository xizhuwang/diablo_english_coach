namespace DiabloEnglishCoach;

internal sealed record SpeechPlayback(long Id, string Text, bool Playing, bool Completed = false);
internal sealed record TranscriptEntry(long Id, string Text, DateTime At, string Status);

// Session-only text of actual playback, never microphone recordings or queued drafts.
internal sealed class SpeechTranscript
{
    private readonly List<TranscriptEntry> _history = new();
    public IReadOnlyList<TranscriptEntry> History => _history;
    public TranscriptEntry? Current { get; private set; }
    public bool Playing { get; private set; }
    private long _hideAt;

    public void Observe(SpeechPlayback playback, long now)
    {
        if (playback.Playing)
        {
            if (Current is not null && playback.Id <= Current.Id) return;
            Current = new(playback.Id, playback.Text, DateTime.Now, "播放中");
            _history.Add(Current);
            while (_history.Count > 30) _history.RemoveAt(0);
            Playing = true;
            _hideAt = long.MaxValue;
        }
        else
        {
            var index = _history.FindIndex(e => e.Id == playback.Id);
            if (index >= 0) _history[index] = _history[index] with { Status = playback.Completed ? "已播完" : "已停止" };
            if (Current?.Id != playback.Id) return;
            Current = Current with { Status = playback.Completed ? "已播完" : "已停止" };
            Playing = false;
            _hideAt = now + 8_000;
        }
    }

    public bool Visible(long now) => Current is not null && (Playing || now < _hideAt);
    public string FullText() => _history.Count == 0 ? "教練開始說話後，逐字稿會出現在這裡。" :
        string.Join("\r\n\r\n", _history.Select(e => $"{e.At:HH:mm:ss} · {e.Status}\r\n{e.Text}"));
}
