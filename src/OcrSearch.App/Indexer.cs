using System.IO;
using OcrSearch.Core.Indexing;
using OcrSearch.Core.Ocr;

namespace OcrSearch.App;

/// <summary>One indexing pass: a fresh snapshot of the configured folders is reconciled against the DB.</summary>
internal static class Indexer
{
    public static async Task RunAsync(
        IndexStore store, IOcrEngine engine, AppSettings settings,
        IProgress<string> status, CancellationToken cancellationToken)
    {
        status.Report("Scanning folders…");
        var files = new List<FileInfo>();
        var enumeration = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        foreach (var root in settings.Folders.Where(Directory.Exists))
        {
            files.AddRange(new DirectoryInfo(root)
                .EnumerateFiles("*", enumeration)
                .Where(f => settings.Extensions.Contains(f.Extension.ToLowerInvariant())));
        }
        // Overlapping roots (a folder plus its own subfolder) must not process the same file twice.
        files = files.DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList();

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
        var pending = files
            .Where(f => !store.IsUpToDate(f.FullName, f.Length, f.LastWriteTimeUtc.Ticks, engineId))
            .ToList();

        var done = 0;
        foreach (var file in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
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
            store.Upsert(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, engineId, text);
            done++;
            status.Report($"Indexing: {done}/{pending.Count} — {file.Name}");
        }

        var summary = $"Index up to date: {store.FileCount()} files";
        if (pending.Count > 0)
        {
            summary += $", {pending.Count} updated";
        }
        status.Report(summary);
    }
}
