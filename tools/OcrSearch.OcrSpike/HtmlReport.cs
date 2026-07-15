using System.Net;
using System.Text;

namespace OcrSearch.OcrSpike;

/// <summary>
/// HTML report of "image + extracted text per language" — for eyeballing OCR quality.
/// </summary>
internal static class HtmlReport
{
    public static void Write(
        string reportPath,
        string target,
        IReadOnlyList<string> languageTags,
        IReadOnlyList<FileResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>OCR spike — report</title>
            <style>
            body{font-family:'Segoe UI',sans-serif;margin:24px;background:#f5f5f5;color:#222}
            h1{font-size:20px}
            .summary{border-collapse:collapse;margin:12px 0 24px}
            .summary td,.summary th{border:1px solid #ccc;padding:4px 10px;text-align:left;font-size:14px}
            .card{display:flex;gap:16px;background:#fff;border:1px solid #ddd;border-radius:8px;padding:12px;margin-bottom:16px;align-items:flex-start}
            .imgbox{flex:0 0 auto;max-width:540px}
            .card img{max-width:520px;max-height:420px;object-fit:contain;border:1px solid #eee;background:#fafafa}
            .meta{font-size:12px;color:#666;margin-top:6px;word-break:break-all}
            .texts{flex:1 1 auto;min-width:0}
            .texts h3{margin:0 0 4px;font-size:13px;color:#444}
            .texts pre{white-space:pre-wrap;word-break:break-word;background:#f8f8f8;border:1px solid #eee;border-radius:4px;padding:8px;font-size:13px;max-height:300px;overflow:auto;margin:0 0 12px}
            .empty{color:#999;font-style:italic}
            .error{color:#b00}
            </style>
            </head>
            <body>
            """);

        sb.AppendLine($"<h1>OCR spike — report</h1>");
        sb.AppendLine($"<p>Target: <code>{WebUtility.HtmlEncode(target)}</code> · files: {results.Count} · generated: {DateTime.Now:yyyy-MM-dd HH:mm}</p>");

        sb.AppendLine("<table class=\"summary\"><tr><th>Language</th><th>Files with text</th><th>Average time</th><th>Errors</th></tr>");
        foreach (var tag in languageTags)
        {
            var outcomes = results
                .Select(r => r.Outcomes.FirstOrDefault(o => o.LanguageTag == tag))
                .Where(o => o is not null)
                .Cast<EngineOutcome>()
                .ToList();
            var ok = outcomes.Where(o => o.Error is null).ToList();
            var withText = ok.Count(o => o.WordCount > 0);
            var avgMs = ok.Count > 0 ? ok.Average(o => o.Duration.TotalMilliseconds) : 0;
            sb.AppendLine(
                $"<tr><td>{WebUtility.HtmlEncode(tag)}</td>" +
                $"<td>{withText}/{outcomes.Count}</td>" +
                $"<td>{avgMs:F0} ms</td>" +
                $"<td>{outcomes.Count - ok.Count}</td></tr>");
        }
        sb.AppendLine("</table>");

        foreach (var result in results)
        {
            var uri = new Uri(result.File.FullName).AbsoluteUri;
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine($"<div class=\"imgbox\"><a href=\"{uri}\" target=\"_blank\"><img src=\"{uri}\" loading=\"lazy\"></a>");
            sb.AppendLine($"<div class=\"meta\">{WebUtility.HtmlEncode(result.File.FullName)} · {result.File.Length / 1024.0:F0} KB · {result.File.LastWriteTime:yyyy-MM-dd HH:mm}</div></div>");
            sb.AppendLine("<div class=\"texts\">");
            foreach (var outcome in result.Outcomes)
            {
                if (outcome.Error is not null)
                {
                    sb.AppendLine($"<h3>{WebUtility.HtmlEncode(outcome.LanguageTag)}</h3>");
                    sb.AppendLine($"<pre class=\"error\">{WebUtility.HtmlEncode(outcome.Error)}</pre>");
                }
                else if (outcome.WordCount == 0)
                {
                    sb.AppendLine($"<h3>{WebUtility.HtmlEncode(outcome.LanguageTag)} · {outcome.Duration.TotalMilliseconds:F0} ms</h3>");
                    sb.AppendLine("<pre class=\"empty\">(no text found)</pre>");
                }
                else
                {
                    sb.AppendLine($"<h3>{WebUtility.HtmlEncode(outcome.LanguageTag)} · words: {outcome.WordCount} · {outcome.Duration.TotalMilliseconds:F0} ms</h3>");
                    sb.AppendLine($"<pre>{WebUtility.HtmlEncode(outcome.Text)}</pre>");
                }
            }
            sb.AppendLine("</div></div>");
        }

        sb.AppendLine("</body></html>");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
    }
}
