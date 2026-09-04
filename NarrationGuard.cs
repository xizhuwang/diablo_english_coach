namespace DiabloEnglishCoach;

// Centralizes the "do not talk over the game" policy. It only decides whether
// a NEW coach paragraph may start; SpeechService deliberately lets a paragraph
// that already started finish, avoiding the old stop/start interruption bug.
internal sealed class NarrationGuard
{
    internal const long DialogueTailMs = 7_000;
    internal const long SpaceTailMs = 5_000;
    internal const long CutsceneTailMs = 8_000;
    private long _blockedUntil;
    private bool _questWasVisible;

    public long BlockedUntil => _blockedUntil;

    public void ObserveDialogue(long now, bool dialogueVisible)
    {
        if (dialogueVisible) Block(now, DialogueTailMs);
    }

    public void ObserveSpaceKey(long now, long spaceKeyIdleMs)
    {
        if (spaceKeyIdleMs < SpaceTailMs) Block(now, SpaceTailMs);
    }

    public void ObserveQuestPanel(long now, bool questVisible)
    {
        // Diablo hides the normal quest tracker during many cinematics. This is
        // a conservative heuristic; a short OCR miss only postpones narration.
        if (_questWasVisible && !questVisible) Block(now, CutsceneTailMs);
        _questWasVisible = questVisible;
    }

    public bool MayStart(long now, bool running, bool settingsOpen, bool speakingPractice,
        bool gameForeground) => running && !settingsOpen && !speakingPractice &&
        gameForeground && now >= _blockedUntil;

    private void Block(long now, long duration) =>
        _blockedUntil = Math.Max(_blockedUntil, now + duration);
}
