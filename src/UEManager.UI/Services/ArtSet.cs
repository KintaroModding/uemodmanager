using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UEManager.Core;

namespace UEManager.UI.Services;

/// <summary>Optional artwork compiled into the exe from Theme\Art. Every image may be missing.</summary>
public sealed record ArtSet(ImageSource? Background, ImageSource? Logo)
{
    public bool HasBackground => Background is not null;

    public bool HasLogo => Logo is not null;

    public static ArtSet Load() => new(LoadImage("background"), LoadImage("logo"));

    private static ImageSource? LoadImage(string name)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri($"pack://application:,,,/Theme/Art/{name}{extension}", UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (IOException)
            {
                // Not built into this exe.
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
            {
                Log.Warn($"Artwork '{name}{extension}' could not be read: {ex.Message}");
            }
        }
        return null;
    }
}
