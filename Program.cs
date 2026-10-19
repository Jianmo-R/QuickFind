using System.Text;

namespace QuickFind;

internal static class Program
{
    private const string MutexName = @"Local\QuickFind.SingleInstance.7A61";
    private const string ShowEventName = @"Local\QuickFind.Show.7A61";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!isFirstInstance)
        {
            // The app is already running: ask it to come to the front, then leave.
            try
            {
                showEvent.Set();
            }
            catch
            {
                // Ignore and simply exit.
            }

            return;
        }

        UsePrivateTempDirectory();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            Application.EnableVisualStyles();
        }
        catch
        {
            // Some locked-down environments deny the temp file used for the visual-styles
            // activation context. The custom-drawn UI still works without it.
        }

        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Application.SetColorMode(SystemColorMode.Dark);
        }
        catch
        {
            // Older runtimes do not support the dark control mode.
        }

        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        var index = FileIndex.Empty;
        try
        {
            using var form = new MainForm(index, showEvent);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    private static void UsePrivateTempDirectory()
    {
        try
        {
            string temp = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickFind",
                "temp");
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("TEMP", temp);
            Environment.SetEnvironmentVariable("TMP", temp);
        }
        catch
        {
            // Fall back to the system temp directory.
        }
    }

    private static void ReportFatal(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickFind");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Logging must never take down the app.
        }

        try
        {
            MessageBox.Show(
                "闪查遇到了一点问题：\n\n" + ex.Message + "\n\n详细日志已写入 %LOCALAPPDATA%\\QuickFind\\error.log",
                "闪查 QuickFind",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Ignore.
        }
    }
}
