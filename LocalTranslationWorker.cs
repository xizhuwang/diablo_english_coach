using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DiabloEnglishCoach;

// One bounded local process, stdin/stdout only; never listens on a network port.
internal sealed class LocalTranslationWorker : IDisposable
{
    private readonly SemaphoreSlim _slot = new(1);
    private Process? _process;
    private int _id;
    private bool _disposed;
    internal int? ProcessId => _process is { HasExited: false } process ? process.Id : null;
    internal static string RuntimePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "translation-runtime"));

    public async Task<string?> TranslateAsync(string text, int threads, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        var responseDrained = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            // Native translation cannot be interrupted safely. Drain this one
            // bounded reply, discard it if obsolete, then serve the newest item.
            // Killing the process on every subtitle change forces a cold reload.
            using var deadline = new CancellationTokenSource();
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            if (_process is null || _process.HasExited)
            {
                Stop();
                var python = Path.Combine(RuntimePath, "python", "python.exe");
                if (!File.Exists(python)) throw new FileNotFoundException("請執行安裝本機翻譯.cmd");
                var start = new ProcessStartInfo(python) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = RuntimePath
                };
                start.ArgumentList.Add("-u");
                start.ArgumentList.Add(Path.Combine(RuntimePath, "translation-worker.py"));
                start.Environment["OMP_NUM_THREADS"] = "2";
                _process = Process.Start(start) ?? throw new IOException("無法啟動地端翻譯");
                _process.ErrorDataReceived += (_, _) => { }; // Drain native diagnostics; never block stderr.
                _process.BeginErrorReadLine();
                try { _process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            }
            var id = ++_id;
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, text, threads }).AsMemory(), deadline.Token);
            await _process.StandardInput.FlushAsync(deadline.Token);
            var response = await _process.StandardOutput.ReadLineAsync(deadline.Token);
            if (response is null || response.Length > 16000) throw new IOException("地端翻譯沒有回應");
            using var json = JsonDocument.Parse(response);
            if (json.RootElement.GetProperty("id").GetInt32() != id) throw new IOException("翻譯回應編號不符");
            responseDrained = true;
            token.ThrowIfCancellationRequested();
            return json.RootElement.TryGetProperty("text", out var value) ? value.GetString() : null;
        }
        catch
        {
            if (!responseDrained) Stop(); // Broken/deadline-expired streams require a clean restart.
            throw;
        }
        finally { _slot.Release(); }
    }

    private void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        process.Dispose();
    }
    public void Dispose() { _disposed = true; Stop(); }
}
