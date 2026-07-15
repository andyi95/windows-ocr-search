namespace OcrSearch.OcrSpike;

internal sealed record EngineOutcome(
    string LanguageTag,
    string Text,
    int WordCount,
    TimeSpan Duration,
    string? Error);

internal sealed record FileResult(FileInfo File, IReadOnlyList<EngineOutcome> Outcomes);
