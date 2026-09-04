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
    private readonly ParagraphSpeechQueue _paragraphs;
    private readonly Dictionary<string, byte[]> _preparedAudio = new();
    public bool IsBusy => _paragraphs.IsBusy;
    public Func<bool>? MayStartPlayback { get; set; }

    public event Action<string>? StatusChanged;

    public SpeechService(CoachConfig config, bool initializeLocalVoice = true)
    {
        _config = config;
        _paragraphs = new ParagraphSpeechQueue(PlayParagraphAsync);
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

    public void SpeakEnglish(string text) => _ = EnqueueSpeech(text, false);

    public void SpeakTraditionalChinese(string text) => _ = EnqueueSpeech(text, true);

    // True only after actual playback ends; never start a mic on a guessed delay.
    public Task<bool> SpeakEnglishAndWaitAsync(string text) => EnqueueSpeech(text, false);
    public Task<bool> SpeakChineseAndWaitAsync(string text) => EnqueueSpeech(text, true);

    private string AudioKey(string text, bool chinese) =>
        $"{(chinese ? _config.ChineseVoice : _config.EnglishVoice)}|{_config.SpeechRatePercent}|{_config.SpeechPitchHz}|{text}";

    public bool IsChineseSpeechPrepared(string text) =>
        !_disposed && (!_config.UseOnlineNeuralVoice || _preparedAudio.ContainsKey(AudioKey(text, true)));

    // Prepare bridge audio during the demonstration, but never play it here.
    // Processing uses it ONLY if ready, so no extra TTS download delays feedback.
    public async Task PrepareChineseSpeechAsync(string text, CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || IsChineseSpeechPrepared(text)) return;
        var key = AudioKey(text, true);
        var path = Path.Combine(Path.GetTempPath(), $"diablo-coach-prepared-{Guid.NewGuid():N}.mp3");
        Task? download = null;
        try
        {
            download = new Communicate(text, voice: _config.ChineseVoice,
                rate: FormatPercent(_config.SpeechRatePercent), pitch: FormatHertz(_config.SpeechPitchHz)).SaveAsync(path);
            await download.WaitAsync(TimeSpan.FromSeconds(12), token);
            if (_disposed || token.IsCancellationRequested || new FileInfo(path).Length > 512_000) return;
            var bytes = await File.ReadAllBytesAsync(path, token);
            if (_disposed || token.IsCancellationRequested) return;
            while (_preparedAudio.Count >= 8) _preparedAudio.Remove(_preparedAudio.Keys.First());
            _preparedAudio[key] = bytes; // Up to 4 MB, session-only, no microphone data.
        }
        catch { /* Optional prefetch must not delay or fault the speaking session. */ }
        finally
        {
            if (download is not null && !download.IsCompleted) _ = CleanLateDownloadAsync(download, path);
            else TryDelete(path);
        }
    }

    private Task<bool> EnqueueSpeech(string text, bool chinese) =>
        _disposed || string.IsNullOrWhiteSpace(text) ? Task.FromResult(false) : _paragraphs.Enqueue(text, chinese);

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        try { _speaker?.Speak(string.Empty, 3); } catch { }
        lock (_playerLock)
        {
            ClosePlayer();
            DeleteCurrentAudio();
        }
        _paragraphs.Clear();
    }

    private async Task<bool> PlayParagraphAsync(string text, bool chinese)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
            return false;

        if (MayStartPlayback?.Invoke() == false)
            return false;

        // Called only by the serial paragraph queue. A new request is not a stop command.
        if (!_config.UseOnlineNeuralVoice)
        {
            var localGeneration = Volatile.Read(ref _generation);
            return SpeakLocal(text, chinese) && await WaitForLocalAsync(localGeneration);
        }

        var generation = Volatile.Read(ref _generation);
        return await SpeakNeuralAsync(text, chinese, generation);
    }

    private async Task<bool> SpeakNeuralAsync(string text, bool chinese, int generation)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diablo-coach-{Guid.NewGuid():N}.mp3");
        Task? download = null;
        try
        {
            var voice = chinese ? _config.ChineseVoice : _config.EnglishVoice;
            var rate = FormatPercent(_config.SpeechRatePercent);
            var pitch = FormatHertz(_config.SpeechPitchHz);
            if (_preparedAudio.TryGetValue(AudioKey(text, chinese), out var prepared))
                await File.WriteAllBytesAsync(path, prepared);
            else
            {
                var request = new Communicate(text, voice: voice, rate: rate, pitch: pitch);
                download = request.SaveAsync(path);
                await download.WaitAsync(TimeSpan.FromSeconds(12));
            }

            // Dialogue/cutscene may have started while neural audio downloaded.
            // Nothing is interrupted after actual playback begins.
            if (MayStartPlayback?.Invoke() == false)
            {
                TryDelete(path);
                return false;
            }

            if (_disposed || generation != Volatile.Read(ref _generation))
            {
                TryDelete(path);
                return false;
            }

            lock (_playerLock)
            {
                if (_disposed || generation != Volatile.Read(ref _generation))
                {
                    TryDelete(path);
                    return false;
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
            while (!_disposed && generation == Volatile.Read(ref _generation))
            {
                await Task.Delay(100);
                lock (_playerLock)
                {
                    if (_disposed || generation != Volatile.Read(ref _generation)) return false;
                    var state = new StringBuilder(32);
                    if (mciSendString($"status {PlayerAlias} mode", state, state.Capacity, nint.Zero) != 0)
                        return false;
                    if (state.ToString().Equals("stopped", StringComparison.OrdinalIgnoreCase))
                    {
                        ClosePlayer();
                        DeleteCurrentAudio();
                        return true;
                    }
                }
            }
            return false;
        }
        catch (Exception exception)
        {
            if (download is not null && !download.IsCompleted)
                _ = CleanLateDownloadAsync(download, path);
            TryDelete(path);
            if (_disposed || generation != Volatile.Read(ref _generation))
                return false;
            Notify($"自然語音連線失敗，已改用離線聲音：{exception.Message}");
            // Invoked from the UI, so captured await context keeps SAPI on its owner thread.
            if (!_disposed && generation == Volatile.Read(ref _generation) && MayStartPlayback?.Invoke() != false)
            {
                return SpeakLocal(text, chinese) && await WaitForLocalAsync(generation);
            }
            return false;
        }
    }

    private static async Task CleanLateDownloadAsync(Task download, string path)
    {
        try { await download; } catch { }
        TryDelete(path); // A late network response never starts playback.
    }

    private async Task<bool> WaitForLocalAsync(int generation)
    {
        try
        {
            do
            {
                await Task.Delay(100);
                if (_disposed || generation != Volatile.Read(ref _generation)) return false;
            } while ((int)_speaker!.Status.RunningState != 1); // SRSEDone
            return true;
        }
        catch { return false; }
    }

    private bool SpeakLocal(string text, bool chinese)
    {
        if (_disposed || _speaker is null)
            return false;

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
            return true;
        }
        catch (Exception exception)
        {
            Notify($"無法朗讀：{exception.Message}");
            return false;
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
        _preparedAudio.Clear();
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
