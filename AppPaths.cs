namespace QuickFind;

internal static class AppPaths
{
    public static string LocalAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataDirectory { get; } = Path.Combine(LocalAppData, "QuickFind");

    public static string IndexPath { get; } = Path.Combine(DataDirectory, "index.bin");

    public static string LastScanPath { get; } = Path.Combine(DataDirectory, "lastscan.txt");

    public static string WebView2DataDirectory { get; } = FindWritableWebView2Directory();

    public static string TempDirectory { get; } = Path.Combine(DataDirectory, "temp");

    private static string FindWritableWebView2Directory()
    {
        string[] candidates =
        {
            Path.Combine(DataDirectory, "WebView2"),
            Path.Combine(AppContext.BaseDirectory, "QuickFind.WebView2"),
            Path.Combine(Path.GetTempPath(), "QuickFind.WebView2")
        };

        foreach (string candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                string probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch
            {
                // Try the next location.
            }
        }

        return candidates[^1];
    }

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
    }
}
