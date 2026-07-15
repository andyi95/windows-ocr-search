using System.IO;

namespace OcrSearch.App;

/// <summary>All MVP parameters are hardcoded here — edit by hand to customize.</summary>
internal static class Config
{
    /// <summary>Folders to index (walked recursively; missing ones are silently skipped).</summary>
    public static readonly string[] Folders =
    [
        // Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lightshot"),
    ];

    /// <summary>Primary OCR language: the ru engine reliably reads Latin text too (confirmed by the phase-0 spike).</summary>
    public const string OcrLanguage = "ru";

    public static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"];

    public static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OcrSearch", "index.db");
}
