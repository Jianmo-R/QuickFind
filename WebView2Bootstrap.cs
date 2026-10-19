using System.Reflection;
using System.Runtime.InteropServices;

namespace QuickFind;

/// <summary>
/// The WebView2 SDK resolves WebView2Loader.dll by name. A single-file publish cannot
/// self-extract native libraries in every environment, so the loader is embedded and
/// written to the app's writable data directory before WebView2 starts.
/// </summary>
internal static class WebView2Bootstrap
{
    private const string ResourceName = "QuickFind.Native.WebView2Loader.dll";
    private static readonly object Sync = new();
    private static bool _loaded;

    public static void EnsureLoader()
    {
        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            string external = Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll");
            if (File.Exists(external))
            {
                TryLoad(external);
                return;
            }

            try
            {
                string targetDirectory = AppPaths.WebView2DataDirectory;
                Directory.CreateDirectory(targetDirectory);
                string target = Path.Combine(targetDirectory, "WebView2Loader.dll");
                using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (resource is null)
                {
                    return;
                }

                bool needsWrite = !File.Exists(target) || new FileInfo(target).Length != resource.Length;
                if (needsWrite)
                {
                    string temp = target + ".tmp";
                    using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(output);
                    }

                    File.Move(temp, target, overwrite: true);
                }

                TryLoad(target);
            }
            catch
            {
                // WebView2 initialization will surface a useful error if loading fails.
            }
        }
    }

    private static void TryLoad(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                NativeLibrary.Load(path);
            }
        }
        catch
        {
            // Ignore; the SDK may still locate its own copy.
        }
    }
}
