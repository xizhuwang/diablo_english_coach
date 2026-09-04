using System.Runtime.InteropServices;

namespace DiabloEnglishCoach;

internal readonly record struct LoadSnapshot(double CpuPercent, int InputIdleMs, bool ShouldDefer)
{
    public string Reason => InputIdleMs < AdaptiveLoadMonitor.ActiveInputThresholdMs
        ? "正在操作"
        : CpuPercent >= AdaptiveLoadMonitor.HighCpuThresholdPercent ? $"CPU {CpuPercent:F0}%" : "低負載";
}

internal sealed class AdaptiveLoadMonitor
{
    internal const int ActiveInputThresholdMs = 2500;
    internal const double HighCpuThresholdPercent = 72;

    private ulong _previousIdle;
    private ulong _previousTotal;
    private bool _hasCpuSample;

    public LoadSnapshot Sample(bool coachBusy, bool explicitInteraction)
    {
        var idleMs = ReadInputIdleMilliseconds();
        var cpuPercent = ReadCpuPercent();
        return new LoadSnapshot(
            cpuPercent,
            idleMs,
            ShouldDefer(cpuPercent, idleMs, coachBusy, explicitInteraction));
    }

    internal static bool ShouldDefer(double cpuPercent, int inputIdleMs, bool coachBusy, bool explicitInteraction) =>
        !explicitInteraction &&
        (coachBusy || inputIdleMs < ActiveInputThresholdMs || cpuPercent >= HighCpuThresholdPercent);

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
        return Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static int ReadInputIdleMilliseconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return int.MaxValue;
        var elapsed = unchecked((uint)Environment.TickCount - info.TickCount);
        return elapsed > int.MaxValue ? int.MaxValue : (int)elapsed;
    }

    private static ulong ToUInt64(NativeFileTime value) => ((ulong)value.High << 32) | value.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
