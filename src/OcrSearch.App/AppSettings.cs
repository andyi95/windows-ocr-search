using System.IO;
using System.Text.Json;

namespace OcrSearch.App;

internal sealed class AppSettings
{
    public string[] Folders { get; set; } =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lightshot"),
    ];

    /// <summary>Primary OCR language: the ru engine reliably reads Latin text too (confirmed by the phase-0 spike).</summary>
    public string OcrLanguage { get; set; } = "ru";

    public string[] Extensions { get; set; } =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Config.SettingsPath), JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config.SettingsPath)!);
        File.WriteAllText(Config.SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
