using System.Runtime.InteropServices;

namespace DiabloEnglishCoach;

internal static class AzureCredentialStore
{
    private const string Target = "DiabloEnglishCoach/AzureTranslator";
    private const uint Generic = 1;
    private const uint LocalMachine = 2;

    public static bool Exists() => !string.IsNullOrWhiteSpace(Read());
    public static string? Read()
    {
        if (!CredRead(Target, Generic, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.Blob == IntPtr.Zero || credential.BlobSize == 0
                ? null : Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
        }
        finally { CredFree(pointer); }
    }
    public static void Write(string secret)
    {
        secret = secret.Trim();
        if (secret.Length is 0 or > 1000) throw new ArgumentException("金鑰長度不合理。", nameof(secret));
        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential { Type = Generic, TargetName = Target, UserName = "Azure Translator F0",
                Persist = LocalMachine, Blob = blob, BlobSize = checked((uint)(secret.Length * 2)) };
            if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public static void Delete()
    {
        if (!CredDelete(Target, Generic, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type; public string TargetName; public string? Comment; public long LastWritten;
        public uint BlobSize; public IntPtr Blob; public uint Persist, AttributeCount; public IntPtr Attributes;
        public string? TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
