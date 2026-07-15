using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OcrSearch.App;

/// <summary>A search result row; the thumbnail loads lazily when the row is displayed.</summary>
public sealed class ResultItem(string path, string snippet)
{
    public string Path { get; } = path;
    public string Snippet { get; } = snippet;
    public string FileName => System.IO.Path.GetFileName(Path);

    private ImageSource? _thumbnail;

    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnail is null)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(Path);
                    // Decode straight to thumbnail size — don't keep full screenshots in memory.
                    image.DecodePixelWidth = 230;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    _thumbnail = image;
                }
                catch
                {
                    // Broken image — the row stays without a thumbnail.
                }
            }
            return _thumbnail;
        }
    }
}
