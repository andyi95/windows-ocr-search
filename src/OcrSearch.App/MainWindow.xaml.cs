using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RunSearch();
        };
        Loaded += (_, _) => StartIndexing();
        Closed += (_, _) =>
        {
            _closed = true;
            _indexCts?.Cancel();
            _store.Dispose();
        };
    }

    private bool Indexing => _indexTask is { IsCompleted: false };

    private async void StartIndexing()
    {
        if (Indexing)
        {
            return;
        }
        var engine = WindowsMediaOcrEngine.TryCreate(_settings.OcrLanguage);
        if (engine is null)
        {
            StatusText.Text = $"Windows OCR pack for language '{_settings.OcrLanguage}' is not installed";
            return;
        }

        _indexCts = new CancellationTokenSource();
        var token = _indexCts.Token;
        ReindexButton.Content = "Stop";
        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            _indexTask = Task.Run(() => Indexer.RunAsync(_store, engine, _settings, progress, token));
            await _indexTask;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Indexing stopped — press Reindex to resume";
        }
        catch (Exception e)
        {
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
            StartIndexing();
        }
    }

    private void Reindex_Click(object sender, RoutedEventArgs e)
    {
        if (Indexing)
        {
            _indexCts!.Cancel();
        }
        else
        {
            StartIndexing();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        _settings = dialog.Result;
        _settings.Save();
        if (Indexing)
        {
            // A running pass keeps the old settings — stop it and restart once it winds down.
            _restartPending = true;
            _indexCts!.Cancel();
        }
        else
        {
            StartIndexing();
        }
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
