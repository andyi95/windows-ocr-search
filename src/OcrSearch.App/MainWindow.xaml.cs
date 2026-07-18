using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using OcrSearch.Core.Indexing;
using OcrSearch.Ocr.Windows;

namespace OcrSearch.App;

public partial class MainWindow : Window
{
    private readonly IndexStore _store = new(Config.DbPath);
    private readonly DispatcherTimer _searchDebounce;
    private AppSettings _settings = AppSettings.Load();
    private CancellationTokenSource? _indexCts;
    private Task? _indexTask;
    private bool _restartPending;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        TaskbarItemInfo = new TaskbarItemInfo();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RunSearch();
        };
        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _closed = true;
            _indexCts?.Cancel();
            _store.Dispose();
        };
    }

    private bool Indexing => _indexTask is { IsCompleted: false };

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckIndexStateAsync(promptForMissingFolders: true, promptForNewFiles: true);
    }

    private async Task CheckIndexStateAsync(bool promptForMissingFolders, bool promptForNewFiles, bool autoIndexNewFiles = false)
    {
        if (Indexing)
        {
            return;
        }

        if (!HasConfiguredFolders())
        {
            HideIndexProgress();
            StatusText.Text = "No folders configured";
            if (promptForMissingFolders
                && PromptDialog.Show(
                    this,
                    "No folders configured",
                    "Choose folders to make screenshots searchable. You can start indexing after saving settings.",
                    "Open settings",
                    "Not now")
                && ShowSettingsDialog())
            {
                await CheckIndexStateAsync(promptForMissingFolders: false, promptForNewFiles: true);
            }
            return;
        }

        HideIndexProgress();
        StatusText.Text = "Checking index…";
        IndexDiff diff;
        try
        {
            diff = await Task.Run(() => Indexer.ReconcileFileList(_store, _settings));
        }
        catch (Exception ex)
        {
            HideIndexProgress();
            StatusText.Text = "Index check error: " + ex.Message;
            return;
        }

        if (diff.NewFiles.Count == 0)
        {
            HideIndexProgress();
            StatusText.Text = $"Index up to date: {diff.IndexedFileCount} files";
            RunSearch();
            return;
        }

        var newPaths = diff.NewFiles.Select(f => f.FullName).ToArray();
        if (autoIndexNewFiles
            || promptForNewFiles
            && PromptDialog.Show(
                this,
                $"{newPaths.Length} new file{(newPaths.Length == 1 ? "" : "s")} found",
                "Start indexing new files now? Existing indexed files will be left unchanged.",
                "Index now",
                "Later"))
        {
            await StartIndexingAsync(newPaths);
        }
        else
        {
            HideIndexProgress();
            StatusText.Text = $"Index has {newPaths.Length} new files pending";
            RunSearch();
        }
    }

    private bool HasConfiguredFolders() =>
        _settings.Folders.Any(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder));

    private async Task StartIndexingAsync(IReadOnlyCollection<string>? pathsToIndex = null)
    {
        if (Indexing)
        {
            return;
        }
        var engine = WindowsMediaOcrEngine.TryCreate(_settings.OcrLanguage);
        if (engine is null)
        {
            HideIndexProgress();
            StatusText.Text = $"Windows OCR pack for language '{_settings.OcrLanguage}' is not installed";
            return;
        }

        _indexCts = new CancellationTokenSource();
        var token = _indexCts.Token;
        ReindexButton.Content = "Stop";
        var progress = new Progress<IndexingProgress>(ApplyIndexProgress);
        try
        {
            _indexTask = Task.Run(() => Indexer.RunAsync(_store, engine, _settings, progress, token, pathsToIndex));
            await _indexTask;
        }
        catch (OperationCanceledException)
        {
            HideIndexProgress();
            StatusText.Text = "Indexing stopped — press Reindex to resume";
        }
        catch (Exception e)
        {
            HideIndexProgress();
            StatusText.Text = "Indexing error: " + e.Message;
        }
        finally
        {
            if (!_closed)
            {
                ReindexButton.Content = "Reindex";
                RunSearch();
            }
        }
        if (_restartPending && !_closed)
        {
            _restartPending = false;
            await CheckIndexStateAsync(promptForMissingFolders: false, promptForNewFiles: true);
        }
    }

    private async void Reindex_Click(object sender, RoutedEventArgs e)
    {
        if (Indexing)
        {
            _indexCts!.Cancel();
        }
        else
        {
            await CheckIndexStateAsync(
                promptForMissingFolders: true,
                promptForNewFiles: false,
                autoIndexNewFiles: true);
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowSettingsDialog())
        {
            return;
        }

        if (Indexing)
        {
            // A running pass keeps the old settings — stop it and re-check once it winds down.
            _restartPending = true;
            _indexCts!.Cancel();
        }
        else
        {
            await CheckIndexStateAsync(promptForMissingFolders: false, promptForNewFiles: true);
        }
    }

    private bool ShowSettingsDialog()
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }
        _settings = dialog.Result;
        _settings.Save();
        return true;
    }

    private void ApplyIndexProgress(IndexingProgress progress)
    {
        StatusText.Text = progress.Message;
        IndexProgressBar.Visibility = Visibility.Visible;
        IndexProgressBar.IsIndeterminate = progress.IsIndeterminate;
        TaskbarItemInfo.ProgressState = progress.IsIndeterminate
            ? TaskbarItemProgressState.Indeterminate
            : TaskbarItemProgressState.Normal;

        if (progress.IsIndeterminate)
        {
            return;
        }

        IndexProgressBar.Maximum = Math.Max(progress.Total, 1);
        IndexProgressBar.Value = Math.Clamp(progress.Completed, 0, (int)IndexProgressBar.Maximum);
        TaskbarItemInfo.ProgressValue = progress.Total > 0
            ? Math.Clamp((double)progress.Completed / progress.Total, 0, 1)
            : 0;

        if (progress.Total == 0 || progress.Completed >= progress.Total)
        {
            HideIndexProgress();
        }
    }

    private void HideIndexProgress()
    {
        IndexProgressBar.IsIndeterminate = false;
        IndexProgressBar.Value = 0;
        IndexProgressBar.Visibility = Visibility.Collapsed;
        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
        TaskbarItemInfo.ProgressValue = 0;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void RunSearch()
    {
        ResultsList.ItemsSource = _store
            .Search(QueryBox.Text, 50)
            .Select(hit => new ResultItem(hit.Path, hit.Snippet))
            .ToList();
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ResultItem item)
        {
            OpenFile(item.Path);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item)
        {
            OpenFile(item.Path);
        }
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item)
        {
            Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item)
        {
            Clipboard.SetText(item.Path);
        }
    }

    private static void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private static ResultItem? ItemFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as ResultItem;
}
