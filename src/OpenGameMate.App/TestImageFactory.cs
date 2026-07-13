using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenGameMate.App;

internal static class TestImageFactory
{
    public static string Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenGameMate");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "phase0-upload.png");

        const int width = 640;
        const int height = 360;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 24, 38)), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(42, 190, 138)), null, new Rect(36, 36, 12, 288));
            var title = new FormattedText(
                "OpenGameMate Phase 0",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"),
                36,
                Brushes.White,
                1.0);
            var subtitle = new FormattedText(
                "Background WebView2 attachment test - no private content",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                19,
                new SolidColorBrush(Color.FromRgb(190, 201, 218)),
                1.0);
            context.DrawText(title, new Point(76, 105));
            context.DrawText(subtitle, new Point(78, 175));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return path;
    }
}
