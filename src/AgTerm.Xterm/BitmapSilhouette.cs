namespace AgTerm.Xterm;

/// <summary>
/// Turns a decoded bitmap icon into a one-color silhouette: each pixel takes the ink and keeps its
/// coverage, so an icon-tinted image file shows its shape in the tint color, as upstream draws a
/// template image.
/// </summary>
public static class BitmapSilhouette
{
    /// <summary>Recolors premultiplied BGRA pixels in place. False, and the pixels unchanged, when the
    /// ink is not a "#rrggbb" color.</summary>
    public static bool Recolor(Span<byte> premultipliedBgra, string ink)
    {
        if (ChromeColor.TryParse(ink) is not { } color) return false;
        for (var i = 0; i + 3 < premultipliedBgra.Length; i += 4)
        {
            var alpha = premultipliedBgra[i + 3];
            premultipliedBgra[i] = Scale(color.B, alpha);
            premultipliedBgra[i + 1] = Scale(color.G, alpha);
            premultipliedBgra[i + 2] = Scale(color.R, alpha);
        }
        return true;
    }

    private static byte Scale(byte channel, byte alpha) => (byte)((channel * alpha + 127) / 255);
}
