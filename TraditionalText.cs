using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DiabloEnglishCoach;

internal static class TraditionalText
{
    // Windows NLS conversion; no network/model or extra service.
    public static string Convert(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Any(c => c is >= '\u3400' and <= '\u9fff')) return text;
        const uint traditionalChinese = 0x04000000;
        var length = LCMapStringEx("zh-CN", traditionalChinese, text, text.Length, null, 0, nint.Zero, nint.Zero, nint.Zero);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var output = new char[length];
        var written = LCMapStringEx("zh-CN", traditionalChinese, text, text.Length, output, length, nint.Zero, nint.Zero, nint.Zero);
        if (written == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return new string(output, 0, written);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength,
        [Out] char[]? destination, int destinationLength, nint version, nint reserved, nint sortHandle);
}
