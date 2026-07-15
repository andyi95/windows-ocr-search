using System.Diagnostics;
using OcrSearch.Core.Ocr;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace OcrSearch.Ocr.Windows;

/// <summary>
/// OCR via the built-in Windows engine (Windows.Media.Ocr).
/// One instance = one recognition language; for mixed text,
/// multiple instances are created and results merged by the caller.
/// </summary>
public sealed class WindowsMediaOcrEngine : IOcrEngine
{
    private readonly OcrEngine _engine;

    public string EngineId { get; }
    public string LanguageTag { get; }

    private WindowsMediaOcrEngine(OcrEngine engine, string languageTag)
    {
        _engine = engine;
        LanguageTag = languageTag;
        EngineId = $"winmedia/{languageTag}/v1";
    }

    public static IReadOnlyList<string> AvailableLanguageTags =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray();

    /// <summary>Returns null if the Windows OCR pack for the language isn't installed.</summary>
    public static WindowsMediaOcrEngine? TryCreate(string languageTag)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
        return engine is null ? null : new WindowsMediaOcrEngine(engine, languageTag);
    }

    public async Task<OcrExtraction> RecognizeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var fileStream = File.OpenRead(filePath);
        using var randomAccessStream = fileStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken);
        using var bitmap = await DecodeWithinOcrLimitsAsync(decoder, cancellationToken);

        var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken);

        var text = string.Join('\n', result.Lines.Select(l => l.Text));
        var wordCount = result.Lines.Sum(l => l.Words.Count);
        return new OcrExtraction(text, wordCount, stopwatch.Elapsed);
    }

    private static async Task<SoftwareBitmap> DecodeWithinOcrLimitsAsync(
        BitmapDecoder decoder, CancellationToken cancellationToken)
    {
        uint maxDimension = OcrEngine.MaxImageDimension;
        if (decoder.PixelWidth <= maxDimension && decoder.PixelHeight <= maxDimension)
        {
            return await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask(cancellationToken);
        }

        // The engine rejects images larger than MaxImageDimension — downscale keeping aspect ratio.
        double scale = Math.Min(
            (double)maxDimension / decoder.PixelWidth,
            (double)maxDimension / decoder.PixelHeight);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)(decoder.PixelWidth * scale),
            ScaledHeight = (uint)(decoder.PixelHeight * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        return await decoder
            .GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);
    }
}
