using System.IO;

namespace OcrSearch.App;

/// <summary>Fixed paths; user-editable parameters live in <see cref="AppSettings"/>.</summary>
internal static class Config
{
    private static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OcrSearch");

    public static string DbPath => Path.Combine(AppDataDir, "index.db");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");
}
