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
    private bool _indexing;

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
        Closed += (_, _) => _store.Dispose();
    }

    private async void StartIndexing()
    {
        if (_indexing)
        {
            return;
        }
        var engine = WindowsMediaOcrEngine.TryCreate(Config.OcrLanguage);
        if (engine is null)
        {
            StatusText.Text = $"Windows OCR pack for language '{Config.OcrLanguage}' is not installed";
            return;
        }

        _indexing = true;
        ReindexButton.IsEnabled = false;
        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            await Task.Run(() => Indexer.RunAsync(_store, engine, progress, CancellationToken.None));
        }
        catch (Exception e)
        {
            StatusText.Text = "Indexing error: " + e.Message;
        }
        finally
        {
            _indexing = false;
            ReindexButton.IsEnabled = true;
            RunSearch();
        }
    }

    private void Reindex_Click(object sender, RoutedEventArgs e) => StartIndexing();

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
