namespace OcrSearch.Core.Ocr;

/// <summary>
/// OCR engine abstraction. Implementations: Windows.Media.Ocr (baseline),
/// with RapidOCR/ONNX planned as a "heavy" fallback for hard images.
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// Stable engine identifier (includes language and logic version).
    /// Stored in the DB next to the extracted text: re-OCR only runs
    /// for files processed by a different/older engine.
    /// </summary>
    string EngineId { get; }

    Task<OcrExtraction> RecognizeAsync(string filePath, CancellationToken cancellationToken = default);
}

public sealed record OcrExtraction(string Text, int WordCount, TimeSpan Duration)
{
    public bool HasText => WordCount > 0;
}
