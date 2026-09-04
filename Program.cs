namespace DiabloEnglishCoach;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            MessageBox.Show(eventArgs.Exception.Message, "Diablo English Coach", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new CoachForm());
    }
}
