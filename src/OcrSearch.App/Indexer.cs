using System.IO;
using System.Security.Cryptography;
using OcrSearch.Core.Indexing;
using OcrSearch.Core.Ocr;

namespace OcrSearch.App;

internal sealed record IndexDiff(IReadOnlyList<FileInfo> NewFiles, int IndexedFileCount);
internal sealed record IndexingProgress(string Message, int Completed = 0, int Total = 0, bool IsIndeterminate = false);

/// <summary>One indexing pass: a fresh snapshot of the configured folders is reconciled against the DB.</summary>
internal static class Indexer
{
    public static IReadOnlyList<FileInfo> EnumerateConfiguredFiles(AppSettings settings)
    {
        var files = new List<FileInfo>();
        var enumeration = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        foreach (var root in settings.Folders.Where(Directory.Exists))
        {
            files.AddRange(new DirectoryInfo(root)
                .EnumerateFiles("*", enumeration)
                .Where(f => settings.Extensions.Contains(f.Extension.ToLowerInvariant())));
        }

        // Overlapping roots (a folder plus its own subfolder) must not process the same file twice.
        return files.DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IndexDiff ReconcileFileList(IndexStore store, AppSettings settings)
    {
        var files = EnumerateConfiguredFiles(settings);
        var onDisk = files.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var indexed = store.GetAllPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var indexedPath in indexed)
        {
            if (!onDisk.Contains(indexedPath))
            {
                store.Remove(indexedPath);
            }
        }

        var newFiles = files
            .Where(f => !indexed.Contains(f.FullName))
            .ToList();

        return new IndexDiff(newFiles, store.FileCount());
    }

    public static async Task RunAsync(
        IndexStore store, IOcrEngine engine, AppSettings settings,
        IProgress<IndexingProgress> status, CancellationToken cancellationToken,
        IReadOnlyCollection<string>? pathsToIndex = null)
    {
        status.Report(new IndexingProgress("Scanning folders…", IsIndeterminate: true));
        var files = EnumerateConfiguredFiles(settings);

        // Files gone from disk are removed from the index so search doesn't return dead paths.
        var onDisk = files.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var indexedPath in store.GetAllPaths())
        {
            if (!onDisk.Contains(indexedPath))
            {
                store.Remove(indexedPath);
            }
        }

        var engineId = $"{engine.EngineId}+{OcrTextCleaner.Version}";
        List<FileInfo> pending;
        if (pathsToIndex is null)
        {
            pending = files
                .Where(f => !store.IsUpToDate(f.FullName, f.Length, f.LastWriteTimeUtc.Ticks, engineId))
                .ToList();
        }
        else
        {
            var targets = pathsToIndex.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var indexed = store.GetAllPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
            pending = files
                .Where(f => targets.Contains(f.FullName) && !indexed.Contains(f.FullName))
                .ToList();
        }

        var done = 0;
        status.Report(new IndexingProgress($"Indexing: 0/{pending.Count}", Total: pending.Count));
        foreach (var file in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            var hash = "";
            try
            {
                hash = ComputeSha256(file.FullName);
                cancellationToken.ThrowIfCancellationRequested();
                text = OcrTextCleaner.Clean((await engine.RecognizeAsync(file.FullName, cancellationToken)).Text);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Unreadable/corrupt file: index it with empty text so it
                // doesn't trip us up again on every run.
                text = "";
            }
            store.Upsert(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, hash, engineId, text);
            done++;
            status.Report(new IndexingProgress($"Indexing: {done}/{pending.Count} — {file.Name}", done, pending.Count));
        }

        var summary = $"Index up to date: {store.FileCount()} files";
        if (pending.Count > 0)
        {
            summary += $", {pending.Count} updated";
        }
        status.Report(new IndexingProgress(summary, pending.Count, pending.Count));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
