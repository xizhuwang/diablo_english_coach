using System.Runtime.InteropServices;
using System.Text;
using EdgeTTS.DotNet;

namespace DiabloEnglishCoach;

internal sealed class SpeechService : IDisposable
{
    private const string PlayerAlias = "diabloCoachVoice";
    private readonly CoachConfig _config;
    private readonly SynchronizationContext? _uiContext;
    private readonly dynamic? _speaker;
    private readonly object _playerLock = new();
    private string? _currentAudioPath;
    private int _generation;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public SpeechService(CoachConfig config, bool initializeLocalVoice = true)
    {
        _config = config;
        _uiContext = SynchronizationContext.Current;
        try
        {
            if (!initializeLocalVoice)
                return;
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is not null)
                _speaker = Activator.CreateInstance(type);
        }
        catch
        {
            _speaker = null;
        }
    }

    public void SpeakEnglish(string text) => StartSpeech(text, false);

    public void SpeakTraditionalChinese(string text) => StartSpeech(text, true);

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        try { _speaker?.Speak(string.Empty, 3); } catch { }
        lock (_playerLock)
        {
            ClosePlayer();
            DeleteCurrentAudio();
        }
    }

    private void StartSpeech(string text, bool chinese)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
            return;

        Stop();
        if (!_config.UseOnlineNeuralVoice)
        {
            SpeakLocal(text, chinese);
            return;
        }

        var generation = Volatile.Read(ref _generation);
        _ = SpeakNeuralAsync(text, chinese, generation);
    }

    private async Task SpeakNeuralAsync(string text, bool chinese, int generation)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diablo-coach-{Guid.NewGuid():N}.mp3");
        try
        {
            var voice = chinese ? _config.ChineseVoice : _config.EnglishVoice;
            var rate = FormatPercent(_config.SpeechRatePercent);
            var pitch = FormatHertz(_config.SpeechPitchHz);
            var request = new Communicate(text, voice: voice, rate: rate, pitch: pitch);
            await request.SaveAsync(path);

            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                TryDelete(path);
                return;
            }

            lock (_playerLock)
            {
                if (_disposed || generation != Volatile.Read(ref _generation))
                {
                    TryDelete(path);
                    return;
                }
                ClosePlayer();
                DeleteCurrentAudio();
                _currentAudioPath = path;
                var error = SendMci($"open \"{path}\" type mpegvideo alias {PlayerAlias}");
                if (error != 0)
                    throw new InvalidOperationException(GetMciError(error));
                error = SendMci($"play {PlayerAlias}");
                if (error != 0)
                    throw new InvalidOperationException(GetMciError(error));
            }
            Notify($"自然語音：{voice} · 語速 {rate}");
        }
        catch (Exception exception)
        {
            TryDelete(path);
            if (_disposed || generation != Volatile.Read(ref _generation))
                return;
            Notify($"自然語音連線失敗，已改用離線聲音：{exception.Message}");
            RunOnUi(() =>
            {
                if (!_disposed && generation == Volatile.Read(ref _generation))
                    SpeakLocal(text, chinese);
            });
        }
    }

    private void SpeakLocal(string text, bool chinese)
    {
        if (_disposed || _speaker is null)
            return;

        try
        {
            var preferredName = chinese ? _config.LocalChineseVoice : _config.LocalEnglishVoice;
            var languageCode = chinese ? "404" : "409";
            dynamic voices = _speaker.GetVoices();
            dynamic? chosen = null;
            for (var index = 0; index < voices.Count; index++)
            {
                dynamic candidate = voices.Item(index);
                var description = (string)candidate.GetDescription();
                if (description.Contains(preferredName, StringComparison.OrdinalIgnoreCase) ||
                    preferredName.Contains(description, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen is null)
            {
                dynamic matchingLanguage = _speaker.GetVoices($"Language={languageCode}");
                if (matchingLanguage.Count > 0)
                    chosen = matchingLanguage.Item(0);
            }

            if (chosen is not null)
                _speaker.Voice = chosen;
            _speaker.Rate = Math.Clamp((int)Math.Round(_config.SpeechRatePercent / 10.0), -10, 10);
            _speaker.Speak(text, 3);
            Notify($"離線語音：{preferredName}");
        }
        catch (Exception exception)
        {
            Notify($"無法朗讀：{exception.Message}");
        }
    }

    private void ClosePlayer() => SendMci($"close {PlayerAlias}");

    private void DeleteCurrentAudio()
    {
        if (_currentAudioPath is null)
            return;
        TryDelete(_currentAudioPath);
        _currentAudioPath = null;
    }

    private static string FormatPercent(int value) => value >= 0 ? $"+{value}%" : $"{value}%";
    private static string FormatHertz(int value) => value >= 0 ? $"+{value}Hz" : $"{value}Hz";

    private void RunOnUi(Action action)
    {
        if (_uiContext is null)
            action();
        else
            _uiContext.Post(_ => action(), null);
    }

    private void Notify(string message) => RunOnUi(() => StatusChanged?.Invoke(message));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static uint SendMci(string command) => mciSendString(command, null, 0, nint.Zero);

    private static string GetMciError(uint code)
    {
        var buffer = new StringBuilder(256);
        return mciGetErrorString(code, buffer, buffer.Capacity) ? buffer.ToString() : $"MCI error {code}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        if (_speaker is not null && Marshal.IsComObject(_speaker))
            Marshal.FinalReleaseComObject(_speaker);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint mciSendString(string command, StringBuilder? returnValue, int returnLength, nint callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool mciGetErrorString(uint errorCode, StringBuilder errorText, int errorTextSize);
}
