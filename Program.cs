namespace DiabloEnglishCoach;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--teaching-test")
        {
            Environment.ExitCode = SelfTest.TestTeachingAsync(Path.GetFullPath(args[1])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }
        if (args.Length >= 2 && args[0] == "--translation-test")
        {
            Environment.ExitCode = FastTranslationSelfTest.LiveAsync(Path.GetFullPath(args[1])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--guide-refresh-test", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = BuildGuideSelfTest.LiveTestAsync(Path.GetFullPath(args[1])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }
        if (args.Length >= 3 && args[0].Equals("--guide-test", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = BuildGuideSelfTest.FileTestAsync(Path.GetFullPath(args[1]),
                Path.GetFullPath(args[2])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }
        if (args.Length >= 3 && args[0].Equals("--speaking-test", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = SpeakingSelfTest.AudioFileAsync(Path.GetFullPath(args[1]),
                Path.GetFullPath(args[2])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }
        if (args.Length > 0 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "self-test.json");
            Environment.ExitCode = SelfTest.RunAsync(outputPath).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }

        if (args.Length >= 4 && args[0].Equals("--voice-sample", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = SelfTest.GenerateVoiceSampleAsync(
                Path.GetFullPath(args[1]), args[2], args[3]).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--ui-preview", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Environment.Exit(SelfTest.CaptureUiPreview(Path.GetFullPath(args[1])) ? 0 : 1);
        }

        if (args.Length >= 3 && args[0].Equals("--screenshot-test", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = SelfTest.TestScreenshotRegionsAsync(
                Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }

        if (args.Length >= 2 && args[0].Equals("--quest-coach-test", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = SelfTest.TestQuestCoachAsync(
                Path.GetFullPath(args[1])).GetAwaiter().GetResult() ? 0 : 1;
            return;
        }

        ApplicationConfiguration.Initialize();
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var anotherInstance = System.Diagnostics.Process.GetProcessesByName("DiabloEnglishCoach")
            .Any(process => { using (process) { return process.Id != current.Id && process.SessionId == current.SessionId; } });
        if (anotherInstance)
        {
            MessageBox.Show("已有另一個教練版本開啟。請先從舊視窗關閉／結束程式，再啟動這個精簡語音版，避免重複掃描與搶 CPU。", "請先關閉舊教練");
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            MessageBox.Show(eventArgs.Exception.Message, "Diablo English Coach", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new CoachForm());
    }
}
