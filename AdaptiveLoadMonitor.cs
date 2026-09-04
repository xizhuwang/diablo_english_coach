using System.Runtime.InteropServices;

namespace DiabloEnglishCoach;

internal readonly record struct LoadSnapshot(double CpuPercent, int ActionKeyIdleMs, bool ShouldDefer)
{
    public string Reason => ActionKeyIdleMs < AdaptiveLoadMonitor.ActiveKeyThresholdMs
        ? "正在操作"
        : CpuPercent >= AdaptiveLoadMonitor.HighCpuThresholdPercent ? $"CPU {CpuPercent:F0}%" : "低負載";
}

internal sealed class AdaptiveLoadMonitor : IDisposable
{
    internal const int ActiveKeyThresholdMs = 2500;
    internal const double HighCpuThresholdPercent = 72;

    private readonly KeyboardActivityMonitor _keyboard = new();
    private ulong _previousIdle;
    private ulong _previousTotal;
    private bool _hasCpuSample;

    public LoadSnapshot Sample(bool coachBusy)
    {
        var actionKeyIdleMs = _keyboard.ActionKeyIdleMilliseconds;
        var cpuPercent = ReadCpuPercent();
        return new LoadSnapshot(
            cpuPercent,
            actionKeyIdleMs,
            ShouldDefer(cpuPercent, actionKeyIdleMs, coachBusy));
    }

    internal static bool ShouldDefer(double cpuPercent, int actionKeyIdleMs, bool coachBusy) =>
        coachBusy || actionKeyIdleMs < ActiveKeyThresholdMs || cpuPercent >= HighCpuThresholdPercent;

    public void Dispose() => _keyboard.Dispose();

    public void SetEnabled(bool enabled) => _keyboard.SetEnabled(enabled);

    private double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;

        var idleValue = ToUInt64(idle);
        var totalValue = ToUInt64(kernel) + ToUInt64(user);
        if (!_hasCpuSample)
        {
            _previousIdle = idleValue;
            _previousTotal = totalValue;
            _hasCpuSample = true;
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var totalDelta = totalValue - _previousTotal;
        _previousIdle = idleValue;
        _previousTotal = totalValue;
        if (totalDelta == 0)
            return 0;
        return Math.Clamp(((double)totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static ulong ToUInt64(NativeFileTime value) => ((ulong)value.High << 32) | value.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

}

internal sealed class KeyboardActivityMonitor : IDisposable
{
    internal const int SpaceVirtualKey = 0x20;
    private const int FirstKeyboardVirtualKey = 0x08;
    private const int LastKeyboardVirtualKey = 0xFE;
    private readonly System.Threading.Timer _timer;
    private long _lastActionKeyTick = Environment.TickCount64 - AdaptiveLoadMonitor.ActiveKeyThresholdMs;

    public KeyboardActivityMonitor()
    {
        // Polling avoids global keyboard hooks. No key code or text is retained;
        // only the time of the latest non-Space keyboard action is stored.
        _timer = new System.Threading.Timer(PollKeyboard, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SetEnabled(bool enabled) => _timer.Change(enabled ? 0 : Timeout.Infinite, enabled ? 60 : Timeout.Infinite);

    public int ActionKeyIdleMilliseconds
    {
        get
        {
            var elapsed = Environment.TickCount64 - Interlocked.Read(ref _lastActionKeyTick);
            return elapsed >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)elapsed);
        }
    }

    internal static bool IsActionKey(int virtualKey) =>
        virtualKey is >= FirstKeyboardVirtualKey and <= LastKeyboardVirtualKey &&
        virtualKey != SpaceVirtualKey;

    public void Dispose() => _timer.Dispose();

    private void PollKeyboard(object? state)
    {
        for (var virtualKey = FirstKeyboardVirtualKey; virtualKey <= LastKeyboardVirtualKey; virtualKey++)
        {
            if (!IsActionKey(virtualKey))
                continue;
            var keyState = GetAsyncKeyState(virtualKey);
            // Read only the current down-state bit. Do not depend on or consume
            // the unreliable "pressed since last call" transition bit.
            if ((keyState & 0x8000) == 0)
                continue;
            Interlocked.Exchange(ref _lastActionKeyTick, Environment.TickCount64);
            return;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
