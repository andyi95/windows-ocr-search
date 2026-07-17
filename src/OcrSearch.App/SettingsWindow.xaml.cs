using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using OcrSearch.Ocr.Windows;

namespace OcrSearch.App;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<string> _folders;

    /// <summary>The edited settings; meaningful when ShowDialog returns true.</summary>
    internal AppSettings Result { get; private set; }

    internal SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Result = current;
        _folders = new ObservableCollection<string>(current.Folders);
        FoldersList.ItemsSource = _folders;

        var languages = WindowsMediaOcrEngine.AvailableLanguageTags.ToList();
        if (!languages.Contains(current.OcrLanguage))
        {
            languages.Insert(0, current.OcrLanguage);
        }
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = current.OcrLanguage;

        ExtensionsBox.Text = string.Join(" ", current.Extensions);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true
            && !_folders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            _folders.Add(dialog.FolderName);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FoldersList.SelectedItem is string folder)
        {
            _folders.Remove(folder);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var extensions = ParseExtensions(ExtensionsBox.Text);
        Result = new AppSettings
        {
            Folders = _folders.ToArray(),
            OcrLanguage = LanguageBox.SelectedItem as string ?? Result.OcrLanguage,
            // An emptied extensions box would wipe the whole index on the next pass — keep the previous list.
            Extensions = extensions.Length > 0 ? extensions : Result.Extensions,
        };
        DialogResult = true;
    }

    private static string[] ParseExtensions(string text) =>
        text.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
            .Distinct()
            .ToArray();
}
