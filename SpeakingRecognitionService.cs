using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using Vosk;

namespace DiabloEnglishCoach;

internal sealed record SpeakingRecording(byte[] Pcm, bool HeardSound);

internal static class SpeakingRecognitionService
{
    public const string ModelDirectoryName = "vosk-model-small-en-us-0.15";
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "speech-model", ModelDirectoryName);
    public static bool ModelAvailable => File.Exists(Path.Combine(ModelPath, "am", "final.mdl")) &&
        File.Exists(Path.Combine(ModelPath, "conf", "model.conf"));

    internal static bool ShouldEndRecording(double seconds, bool heard, double lastSound) =>
        seconds >= 10 || (!heard && seconds >= 5) || (heard && seconds - lastSound >= 1.2);

    // On-demand, default microphone only. No loopback, files, upload or permanent stream.
    // Dispose closes the device BEFORE returning audio to the decoder.
    public static async Task<SpeakingRecording> RecordAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var pcm = new MemoryStream();
        using var capture = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        var gate = new object();
        var acceptingAudio = true;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = Stopwatch.StartNew();
        double lastSound = 0;
        var heard = false;
        capture.DataAvailable += (_, args) =>
        {
            lock (gate)
            {
                if (!acceptingAudio || token.IsCancellationRequested || pcm.Length >= 320_000) return;
                pcm.Write(args.Buffer, 0, Math.Min(args.BytesRecorded, (int)(320_000 - pcm.Length)));
                double energy = 0;
                for (var i = 0; i + 1 < args.BytesRecorded; i += 2)
                {
                    var sample = BitConverter.ToInt16(args.Buffer, i) / 32768d;
                    energy += sample * sample;
                }
                // Energy is only an endpoint heuristic, NOT a speech detector or grade.
                if (args.BytesRecorded > 1 && Math.Sqrt(energy / (args.BytesRecorded / 2)) > 0.012)
                {
                    heard = true;
                    lastSound = clock.Elapsed.TotalSeconds;
                }
            }
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null) stopped.TrySetException(args.Exception);
            else stopped.TrySetResult();
        };
        capture.StartRecording();
        try
        {
            while (true)
            {
                await Task.Delay(100, token);
                if (stopped.Task.IsCompleted) { await stopped.Task; break; }
                lock (gate)
                {
                    var seconds = clock.Elapsed.TotalSeconds;
                    if (ShouldEndRecording(seconds, heard, lastSound)) break;
                }
            }
        }
        finally
        {
            lock (gate) acceptingAudio = false;
            capture.StopRecording();
            // Dispose also closes a device that fails to signal RecordingStopped.
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        token.ThrowIfCancellationRequested();
        lock (gate) return new(pcm.ToArray(), heard);
    }

    public static Task<string> RecognizeAsync(byte[] pcm, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!ModelAvailable) throw new InvalidOperationException("找不到口說模型，請重新安裝 speech-model 資料夾。");
        Vosk.Vosk.SetLogLevel(-1);
        // Release the model after each short exercise: no resident recognizer during combat.
        // Native model loading/one decode chunk cannot be interrupted mid-call.
        using var model = new Model(ModelPath);
        token.ThrowIfCancellationRequested();
        using var recognizer = new VoskRecognizer(model, 16000);
        var texts = new List<string>();
        var chunk = new byte[3200];
        for (var offset = 0; offset < pcm.Length; offset += chunk.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(chunk.Length, pcm.Length - offset);
            Buffer.BlockCopy(pcm, offset, chunk, 0, count);
            if (recognizer.AcceptWaveform(chunk, count)) texts.Add(ReadText(recognizer.Result()));
        }
        token.ThrowIfCancellationRequested();
        texts.Add(ReadText(recognizer.FinalResult()));
        return string.Join(" ", texts.Where(text => text.Length > 0));
    }, token);

    private static string ReadText(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("text", out var value) ? value.GetString() ?? "" : "";
    }
}
