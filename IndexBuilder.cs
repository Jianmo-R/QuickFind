using System.Diagnostics;

namespace QuickFind;

public readonly record struct IndexProgress(int Count, string Drive, string Directory);

public sealed record IndexBuildResult(List<string> Paths, ulong Signature, int SkippedDirectories);

/// <summary>
/// Walks the local fixed/removable drives and collects file paths. The walk is intentionally
/// forgiving: inaccessible and reparse-point directories are skipped rather than aborting the scan.
/// </summary>
public static class IndexBuilder
{
    private static readonly EnumerationOptions FileOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
        MatchType = MatchType.Simple
    };

    private static readonly EnumerationOptions DirectoryOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Simple
    };

    public static IndexBuildResult Build(IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
    {
        ThreadPriority previous = Thread.CurrentThread.Priority;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        }
        catch
        {
            // Priority changes are best-effort.
        }

        try
        {
            return BuildCore(progress, cancellationToken);
        }
        finally
        {
            try
            {
                Thread.CurrentThread.Priority = previous;
            }
            catch
            {
                // Ignore.
            }
        }
    }

    private static IndexBuildResult BuildCore(IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
    {
        var paths = new List<string>(1_000_000);
        ulong signature = 0;
        int skipped = 0;
        int count = 0;
        long lastReport = 0;
        var clock = Stopwatch.StartNew();

        foreach (DriveInfo drive in GetSearchableDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root = drive.RootDirectory.FullName;
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = stack.Pop();

                try
                {
                    foreach (string subdirectory in Directory.EnumerateDirectories(current, "*", DirectoryOptions))
                    {
                        if (ShouldSkipDirectory(subdirectory))
                        {
                            skipped++;
                            continue;
                        }

                        stack.Push(subdirectory);
                    }
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    skipped++;
                }

                try
                {
                    foreach (string file in Directory.EnumerateFiles(current, "*", FileOptions))
                    {
                        if ((count & 0x3FF) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (clock.ElapsedMilliseconds - lastReport >= 160)
                            {
                                lastReport = clock.ElapsedMilliseconds;
                                progress?.Report(new IndexProgress(count, root, current));
                            }
                        }

                        paths.Add(file);
                        signature ^= Mix(Fnv1a(file));
                        count++;
                    }
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    skipped++;
                }
            }

            progress?.Report(new IndexProgress(count, root, root));
        }

        signature ^= (ulong)count * 0x9E3779B97F4A7C15UL;
        if (signature == 0)
        {
            signature = 1;
        }

        progress?.Report(new IndexProgress(count, string.Empty, string.Empty));
        return new IndexBuildResult(paths, signature, skipped);
    }

    public static IEnumerable<DriveInfo> GetSearchableDrives()
    {
        foreach (DriveInfo drive in GetDrivesSafe())
        {
            bool usable;
            try
            {
                usable = drive.IsReady &&
                         drive.DriveType is DriveType.Fixed or DriveType.Removable &&
                         drive.RootDirectory.Exists;
            }
            catch
            {
                usable = false;
            }

            if (usable)
            {
                yield return drive;
            }
        }
    }

    private static DriveInfo[] GetDrivesSafe()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch
        {
            return Array.Empty<DriveInfo>();
        }
    }

    private static bool ShouldSkipDirectory(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
               || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Config.Msi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException;

    private static ulong Fnv1a(string value)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 33;
        value *= 0xFF51AFD7ED558CCDUL;
        value ^= value >> 33;
        value *= 0xC4CEB9FE1A85EC53UL;
        value ^= value >> 33;
        return value;
    }
}
