using System.IO.Compression;
using System.Text;

namespace QuickFind;

public readonly record struct FileEntry(string Path, int NameStart)
{
    public int NameLength => Path.Length - NameStart;
    public ReadOnlySpan<char> Name => Path.AsSpan(NameStart);
}

public readonly record struct SearchHit(string Path, int NameStart, int Score)
{
    public int NameLength => Path.Length - NameStart;
    public ReadOnlySpan<char> Name => Path.AsSpan(NameStart);
    public string NameText => Path[NameStart..];
    public string Folder => NameStart > 0 ? Path[..(NameStart - 1)] : Path;
}

public sealed record SearchResult(SearchHit[] Hits, int TotalMatches);

/// <summary>
/// An immutable snapshot of the file-name index. Searches are lock-free and can run on a
/// background thread while a new snapshot is being built.
/// </summary>
public sealed class FileIndex
{
    private const uint Magic = 0x51464944; // "QFID"
    private const int FormatVersion = 2;

    private FileEntry[] _entries;

    private FileIndex(FileEntry[] entries, ulong signature)
    {
        _entries = entries;
        Signature = signature;
    }

    public static FileIndex Empty { get; } = new(Array.Empty<FileEntry>(), 0);

    public int Count => Volatile.Read(ref _entries).Length;

    public ulong Signature { get; }

    public static FileIndex FromPaths(List<string> paths, ulong signature)
    {
        var entries = new FileEntry[paths.Count];
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            int slash = path.LastIndexOf('\\');
            entries[i] = new FileEntry(path, slash >= 0 ? slash + 1 : 0);
        }

        return new FileIndex(entries, signature);
    }

    public SearchResult Search(
        string rawQuery, int limit, CancellationToken cancellationToken, bool nameOnly = false)
    {
        string query = rawQuery.Trim();
        if (query.Length == 0)
        {
            return new SearchResult(Array.Empty<SearchHit>(), 0);
        }

        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return new SearchResult(Array.Empty<SearchHit>(), 0);
        }

        FileEntry[] entries = Volatile.Read(ref _entries);
        int wanted = Math.Max(1, limit);
        var heap = new PriorityQueue<SearchHit, SearchHit>(wanted + 1, SearchHitWorstFirst.Instance);
        int totalMatches = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            if ((i & 0x1FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ref readonly FileEntry entry = ref entries[i];
            ReadOnlySpan<char> name = entry.Name;
            int score = 0;
            bool allTermsMatch = true;

            foreach (string term in terms)
            {
                int index = name.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    score += 120;
                    if (index == 0)
                    {
                        score += 90;
                    }

                    if (index + term.Length == name.Length)
                    {
                        score += 35;
                    }

                    score -= Math.Min(index, 24);
                    continue;
                }

                // A term may also match a folder in the full path.
                if (!nameOnly &&
                    entry.Path.AsSpan().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 26;
                    continue;
                }

                allTermsMatch = false;
                break;
            }

            if (!allTermsMatch)
            {
                continue;
            }

            int nameLength = entry.NameLength;
            score += nameLength switch
            {
                <= 8 => 70,
                <= 16 => 45,
                <= 28 => 22,
                _ => 0
            };

            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 900;
            }
            else if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 420;
            }
            else if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 160;
            }

            score -= Math.Min(70, entry.Path.Length / 5);
            totalMatches++;

            var hit = new SearchHit(entry.Path, entry.NameStart, score);
            if (heap.Count < wanted)
            {
                heap.Enqueue(hit, hit);
            }
            else if (SearchHitWorstFirst.Instance.Compare(hit, heap.Peek()) > 0)
            {
                heap.DequeueEnqueue(hit, hit);
            }
        }

        SearchHit[] hits = new SearchHit[heap.Count];
        int n = 0;
        foreach ((SearchHit element, _) in heap.UnorderedItems)
        {
            hits[n++] = element;
        }

        Array.Sort(hits, (a, b) => SearchHitWorstFirst.Instance.Compare(b, a));
        return new SearchResult(hits, totalMatches);
    }

    /// <summary>
    /// Finds files related to AI-expanded keywords while excluding files whose names already
    /// contain every term of the user's direct query.
    /// </summary>
    public SearchResult SearchRelated(
        IReadOnlyList<string> relatedTerms,
        IReadOnlyList<string> directTerms,
        int limit,
        CancellationToken cancellationToken)
    {
        string[] terms = relatedTerms
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .Where(term => term.Length >= 2 || IsCjk(term[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        if (terms.Length == 0)
        {
            return new SearchResult(Array.Empty<SearchHit>(), 0);
        }

        string[] direct = directTerms
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        FileEntry[] entries = Volatile.Read(ref _entries);
        int wanted = Math.Max(1, limit);
        var heap = new PriorityQueue<SearchHit, SearchHit>(wanted + 1, SearchHitWorstFirst.Instance);
        int totalMatches = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            if ((i & 0x1FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ref readonly FileEntry entry = ref entries[i];
            ReadOnlySpan<char> name = entry.Name;

            if (direct.Length > 0 && IsDirectNameMatch(name, direct))
            {
                continue;
            }

            int score = 0;
            int matched = 0;

            foreach (string term in terms)
            {
                if (ContainsRelatedTerm(name, term))
                {
                    matched++;
                    score += 105;
                    int index = name.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (index == 0)
                    {
                        score += 45;
                    }

                    score -= Math.Min(index, 20);
                    continue;
                }

                // For AI matches, keep folder-path matches only for distinctive terms.
                if ((term.Any(IsCjk) || term.Length >= 5) &&
                    ContainsRelatedTerm(entry.Path.AsSpan(), term))
                {
                    matched++;
                    score += 18;
                }
            }

            if (matched == 0)
            {
                continue;
            }

            score += matched * 55;
            if (matched >= 2)
            {
                score += 120;
            }

            if (IsProgramPath(entry.Path))
            {
                score -= 55;
            }

            score += entry.NameLength switch
            {
                <= 8 => 45,
                <= 16 => 28,
                <= 28 => 12,
                _ => 0
            };
            score -= Math.Min(50, entry.Path.Length / 7);
            totalMatches++;

            var hit = new SearchHit(entry.Path, entry.NameStart, score);
            if (heap.Count < wanted)
            {
                heap.Enqueue(hit, hit);
            }
            else if (SearchHitWorstFirst.Instance.Compare(hit, heap.Peek()) > 0)
            {
                heap.DequeueEnqueue(hit, hit);
            }
        }

        SearchHit[] hits = new SearchHit[heap.Count];
        int n = 0;
        foreach ((SearchHit element, _) in heap.UnorderedItems)
        {
            hits[n++] = element;
        }

        Array.Sort(hits, (a, b) => SearchHitWorstFirst.Instance.Compare(b, a));
        return new SearchResult(hits, totalMatches);
    }

    private static bool IsDirectNameMatch(ReadOnlySpan<char> name, string[] directTerms)
    {
        foreach (string term in directTerms)
        {
            if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsRelatedTerm(ReadOnlySpan<char> text, string term)
    {
        if (term.Any(IsCjk))
        {
            return text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        int searchStart = 0;
        while (searchStart <= text.Length - term.Length)
        {
            int index = text[searchStart..].IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            index += searchStart;
            bool leftBoundary = index == 0 || !IsAsciiWordChar(text[index - 1]);
            int end = index + term.Length;
            bool rightBoundary = end >= text.Length || !IsAsciiWordChar(text[end]);
            if (leftBoundary && rightBoundary)
            {
                return true;
            }

            searchStart = index + 1;
        }

        return false;
    }

    private static bool IsProgramPath(string path) =>
        path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"C:\ProgramData", StringComparison.OrdinalIgnoreCase);

    private static bool IsCjk(char value) => value >= '\u2E80';

    private static bool IsAsciiWordChar(char value) =>
        value < 128 && char.IsLetterOrDigit(value);

    public static FileIndex? TryLoad(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var file = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.UTF8);

            if (reader.ReadUInt32() != Magic)
            {
                return null;
            }

            if (reader.ReadInt32() != FormatVersion)
            {
                return null;
            }

            ulong signature = reader.ReadUInt64();
            var entries = new List<FileEntry>(1_000_000);

            while (true)
            {
                if ((entries.Count & 0x3FFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                string path = reader.ReadString();
                if (path.Length == 0)
                {
                    break;
                }

                int slash = path.LastIndexOf('\\');
                entries.Add(new FileEntry(path, slash >= 0 ? slash + 1 : 0));
            }

            return new FileIndex(entries.ToArray(), signature);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string filePath, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = filePath + ".tmp";
        FileEntry[] entries = Volatile.Read(ref _entries);

        using (var file = new FileStream(
                   tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(Signature);
            for (int i = 0; i < entries.Length; i++)
            {
                if ((i & 0x3FFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                writer.Write(entries[i].Path);
            }

            writer.Write(string.Empty);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    private sealed class SearchHitWorstFirst : IComparer<SearchHit>
    {
        public static readonly SearchHitWorstFirst Instance = new();

        public int Compare(SearchHit a, SearchHit b)
        {
            int c = a.Score.CompareTo(b.Score);
            if (c != 0)
            {
                return c;
            }

            // A shorter name and a shorter path are slightly better when scores tie.
            c = b.NameLength.CompareTo(a.NameLength);
            if (c != 0)
            {
                return c;
            }

            c = b.Path.Length.CompareTo(a.Path.Length);
            if (c != 0)
            {
                return c;
            }

            return string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
