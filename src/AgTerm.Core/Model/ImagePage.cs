using System.Net;

namespace AgTerm.Core.Model;

/// <summary>The page an image overlay shows and the size it opens at (upstream ImagePage, #844). The
/// natural size is one image pixel per pixel of the display the window is on, times the page zoom.</summary>
public static class ImagePage
{
    /// <summary>The percent an auto-sized page carries beside its points.</summary>
    public const int BasePercent = 95;

    /// <summary>The narrowest auto-sized panel, wide enough for the strip's name and close button.</summary>
    public const double MinimumPoints = 240.0;

    /// <summary>The largest file a page shows; a larger one is refused before it is read.</summary>
    public const long MaxBytes = 64L << 20;

    /// <summary>Refuses a picture whose header promises more pixels than this, a decompression bomb's tell.</summary>
    public const long MaxPixels = 100_000_000;

    public static bool AllowsPixels(int width, int height) =>
        width >= 0 && height >= 0 && (long)width * height <= MaxPixels;

    /// <summary>The PNG, JPEG, GIF or WebP signature at the start of data, null for anything else.</summary>
    public static string? MimeType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return "image/gif";
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    /// <summary>The picture's drawn size in points. EXIF orientations 5 to 8 turn it a quarter, so the
    /// axes swap.</summary>
    public static (double Width, double Height) Natural(int pixelWidth, int pixelHeight, int orientation, double scale)
    {
        if (!(scale > 0)) scale = 1;
        var rotated = orientation is >= 5 and <= 8;
        var (width, height) = rotated ? (pixelHeight, pixelWidth) : (pixelWidth, pixelHeight);
        return (width / scale, height / scale);
    }

    /// <summary>The overlay content size for a picture of natural size at the page zoom, in the
    /// picture's own aspect: a narrow picture is scaled up to MinimumPoints wide, and inside bounds,
    /// the session's area, a picture past the point cap is scaled down until both axes fit. The cap
    /// wins over the minimum.</summary>
    public static OverlayPointSize Points((double Width, double Height) natural, double zoom, HudRect? bounds = null)
    {
        var width = natural.Width * zoom;
        var height = natural.Height * zoom;
        if (!(width > 0) || !(height > 0)) return new OverlayPointSize((int)MinimumPoints, (int)MinimumPoints);
        if (width < MinimumPoints) (width, height) = (MinimumPoints, height * MinimumPoints / width);
        if (bounds is { } area)
        {
            var roomWidth = area.Width * OverlayPointSize.MaxShare;
            var roomHeight = area.Height * OverlayPointSize.MaxShare - HtmlOverlay.ChromeHeight;
            var fit = Math.Min(1, Math.Min(roomWidth / width, roomHeight / height));
            if (fit > 0) (width, height) = (width * fit, height * fit);
        }
        return new OverlayPointSize(Math.Max(1, (int)Math.Round(width, MidpointRounding.AwayFromZero)),
                                    Math.Max(1, (int)Math.Round(height, MidpointRounding.AwayFromZero)));
    }

    /// <summary>A page showing the picture at source filling it with its aspect kept, centered on a
    /// transparent canvas. Upstream embeds the bytes as a data: URL; the port serves them from the
    /// page's own host, since a web view takes at most 2 MB of text.</summary>
    public static string Html(string source) =>
        """
        <!doctype html><html><head><meta charset="utf-8"><style>
        html,body{margin:0;height:100%;background:transparent;overflow:hidden}
        img{display:block;width:100%;height:100%;object-fit:contain}
        </style></head><body><img alt="" src="
        """ + WebUtility.HtmlEncode(source) + "\"></body></html>";
}
