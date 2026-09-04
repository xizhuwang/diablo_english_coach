namespace DiabloEnglishCoach;

internal sealed class SpeechService : IDisposable
{
    private readonly dynamic? _speaker;

    public SpeechService()
    {
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is not null)
                _speaker = Activator.CreateInstance(type);
        }
        catch
        {
            _speaker = null;
        }
    }

    public void SpeakEnglish(string text) => Speak(text, "409");

    public void SpeakTraditionalChinese(string text) => Speak(text, "404");

    public void Stop()
    {
        try { _speaker?.Speak(string.Empty, 3); } catch { }
    }

    private void Speak(string text, string languageCode)
    {
        if (_speaker is null || string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            var voices = _speaker.GetVoices($"Language={languageCode}");
            if (voices.Count > 0)
                _speaker.Voice = voices.Item(0);
            _speaker.Rate = languageCode == "409" ? -2 : 0;
            _speaker.Speak(text, 3); // async + purge previous speech
        }
        catch
        {
            // Speech is optional; OCR and coaching continue if a voice is unavailable.
        }
    }

    public void Dispose()
    {
        Stop();
        if (_speaker is not null && System.Runtime.InteropServices.Marshal.IsComObject(_speaker))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_speaker);
    }
}
