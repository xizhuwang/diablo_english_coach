using System.Runtime.InteropServices;
using System.Text;

namespace DiabloEnglishCoach;

internal static class NativeMethods
{
    internal const uint WdaExcludeFromCapture = 0x00000011;

    internal delegate bool EnumWindowsProc(nint windowHandle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowDisplayAffinity(nint windowHandle, uint affinity);
}
