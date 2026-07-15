using System.Text;
using OcrSearch.Ocr.Windows;
using OcrSearch.OcrSpike;

Console.OutputEncoding = Encoding.UTF8;

var options = SpikeOptions.Parse(args);
if (options is null)
{
    Console.WriteLine("""
        OCR quality spike: runs Windows.Media.Ocr over images and builds an HTML report.

        Usage: OcrSearch.OcrSpike <folder|file> [options]
          --lang ru,en-US   Comma-separated OCR languages (default: all installed)
          --out <file>      Path to the HTML report (default: ocr-spike-report.html)
          --limit N         Max files, newest first (default: 300)
          --no-recurse      Don't descend into subfolders
        """);
    return 1;
}

var requestedTags = options.Languages ?? WindowsMediaOcrEngine.AvailableLanguageTags;
var engines = new List<WindowsMediaOcrEngine>();
foreach (var tag in requestedTags)
{
    var engine = WindowsMediaOcrEngine.TryCreate(tag);
    if (engine is null)
    {
        Console.WriteLine($"[!] Windows OCR pack for language '{tag}' is not installed. " +
                          $"Available: {string.Join(", ", WindowsMediaOcrEngine.AvailableLanguageTags)}");
    }
    else
    {
        engines.Add(engine);
    }
}
if (engines.Count == 0)
{
    Console.WriteLine("Couldn't create any OCR engine — exiting.");
    return 1;
}
Console.WriteLine($"OCR engines: {string.Join(", ", engines.Select(e => e.LanguageTag))}");

string[] imageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"];
List<FileInfo> files;
if (File.Exists(options.Target))
{
    files = [new FileInfo(options.Target)];
}
else if (Directory.Exists(options.Target))
{
    var enumeration = new EnumerationOptions
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = options.Recurse,
    };
    files = new DirectoryInfo(options.Target)
        .EnumerateFiles("*", enumeration)
        .Where(f => imageExtensions.Contains(f.Extension.ToLowerInvariant()))
        .OrderByDescending(f => f.LastWriteTimeUtc)
        .ToList();
}
else
{
    Console.WriteLine($"Path not found: {options.Target}");
    return 1;
}

if (files.Count == 0)
{
    Console.WriteLine("No images found.");
    return 1;
}
if (files.Count > options.Limit)
{
    Console.WriteLine($"Found {files.Count} files; taking the {options.Limit} newest (raise with: --limit N).");
    files = files.Take(options.Limit).ToList();
}
Console.WriteLine($"Files to process: {files.Count}\n");

var results = new List<FileResult>();
var index = 0;
foreach (var file in files)
{
    index++;
    var outcomes = new List<EngineOutcome>();
    foreach (var engine in engines)
    {
        try
        {
            var extraction = await engine.RecognizeAsync(file.FullName);
            outcomes.Add(new EngineOutcome(
                engine.LanguageTag, extraction.Text, extraction.WordCount, extraction.Duration, null));
        }
        catch (Exception e)
        {
            outcomes.Add(new EngineOutcome(engine.LanguageTag, "", 0, TimeSpan.Zero, e.Message));
        }
    }
    results.Add(new FileResult(file, outcomes));

    var parts = outcomes.Select(o => o.Error is null
        ? $"{o.LanguageTag}: words {o.WordCount}, {o.Duration.TotalMilliseconds:F0} ms"
        : $"{o.LanguageTag}: ERROR ({o.Error})");
    Console.WriteLine($"[{index}/{files.Count}] {file.Name} — {string.Join("; ", parts)}");

    var best = outcomes.Where(o => o.Error is null).MaxBy(o => o.WordCount);
    if (best is { WordCount: > 0 })
    {
        Console.WriteLine($"    «{Preview(best.Text, 100)}»");
    }
}

Console.WriteLine("\n=== Summary ===");
foreach (var engine in engines)
{
    var outcomes = results.Select(r => r.Outcomes.First(o => o.LanguageTag == engine.LanguageTag)).ToList();
    var ok = outcomes.Where(o => o.Error is null).ToList();
    var withText = ok.Count(o => o.WordCount > 0);
    var avgMs = ok.Count > 0 ? ok.Average(o => o.Duration.TotalMilliseconds) : 0;
    Console.WriteLine($"{engine.LanguageTag,-8} text found: {withText}/{results.Count}, " +
                      $"average time: {avgMs:F0} ms, errors: {outcomes.Count - ok.Count}");
}

var reportPath = Path.GetFullPath(options.ReportPath);
HtmlReport.Write(reportPath, options.Target, engines.Select(e => e.LanguageTag).ToList(), results);
Console.WriteLine($"\nHTML report: {reportPath}");
return 0;

static string Preview(string text, int maxLength)
{
    var oneLine = text.ReplaceLineEndings(" ⏎ ");
    return oneLine.Length <= maxLength ? oneLine : oneLine[..maxLength] + "…";
}

internal sealed record SpikeOptions(
    string Target,
    IReadOnlyList<string>? Languages,
    string ReportPath,
    int Limit,
    bool Recurse)
{
    public static SpikeOptions? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        string? target = null;
        IReadOnlyList<string>? languages = null;
        var reportPath = "ocr-spike-report.html";
        var limit = 300;
        var recurse = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lang" when i + 1 < args.Length:
                    languages = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--out" when i + 1 < args.Length:
                    reportPath = args[++i];
                    break;
                case "--limit" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed):
                    limit = parsed;
                    i++;
                    break;
                case "--no-recurse":
                    recurse = false;
                    break;
                default:
                    if (target is not null || args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        return null;
                    }
                    target = args[i];
                    break;
            }
        }

        return target is null ? null : new SpikeOptions(target, languages, reportPath, limit, recurse);
    }
}
