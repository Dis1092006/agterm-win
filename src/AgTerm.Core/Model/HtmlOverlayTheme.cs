namespace AgTerm.Core.Model;

/// <summary>The terminal theme as a page sees it (upstream HtmlOverlayTheme, c63e5158). Every page
/// gets it as custom properties (--agterm-background, --agterm-foreground, --agterm-color-0 to 15)
/// that apply nothing until the page uses them; a themed page also gets the color scheme and text
/// color as its default look. All of it sits at zero specificity.</summary>
public sealed class HtmlOverlayTheme : IEquatable<HtmlOverlayTheme>
{
    /// <summary>Takes #rrggbb colors; anything else falls back to a plain dark or light pair,
    /// because the values end up inside a stylesheet.</summary>
    public HtmlOverlayTheme(string background, string foreground, bool dark, IReadOnlyList<string>? palette = null)
    {
        var valid = WatermarkConfig.IsValidColorHex(background) && WatermarkConfig.IsValidColorHex(foreground);
        Background = valid ? background : (dark ? "#1e1e1e" : "#ffffff");
        Foreground = valid ? foreground : (dark ? "#d4d4d4" : "#1e1e1e");
        Dark = dark;
        Palette = palette is { Count: 16 }
            ? palette.Select(color => WatermarkConfig.IsValidColorHex(color) ? color : "").ToArray()
            : [];
    }

    public string Background { get; }

    public string Foreground { get; }

    public bool Dark { get; }

    /// <summary>The 16 ANSI colors by slot, an invalid entry kept empty so the others keep their
    /// index; empty when the theme did not supply exactly 16.</summary>
    public IReadOnlyList<string> Palette { get; }

    public string Stylesheet(bool themed)
    {
        var look = themed ? $"color-scheme: {(Dark ? "dark" : "light")}; color: {Foreground}; " : "";
        var slots = string.Concat(Palette.Select((color, slot) => color.Length == 0 ? "" : $"--agterm-color-{slot}: {color}; "));
        return $":where(html) {{ {look}--agterm-background: {Background}; --agterm-foreground: {Foreground}; {slots}}}";
    }

    /// <summary>Adds the stylesheet to the document as a constructed sheet; it runs at document
    /// creation, before the page's own styles. Upstream prepends a style element instead, but
    /// WebView2 runs this script before the document has a root element and, with page scripts off,
    /// runs no callback that could wait for one.</summary>
    public string Script(bool themed) =>
        "(() => {\n" +
        "  const sheet = new CSSStyleSheet();\n" +
        $"  sheet.replaceSync('{Stylesheet(themed)}');\n" +
        "  document.adoptedStyleSheets = [sheet, ...document.adoptedStyleSheets];\n" +
        "})();";

    public bool Equals(HtmlOverlayTheme? other) =>
        other is not null && Background == other.Background && Foreground == other.Foreground
        && Dark == other.Dark && Palette.SequenceEqual(other.Palette);

    public override bool Equals(object? obj) => Equals(obj as HtmlOverlayTheme);

    public override int GetHashCode() => HashCode.Combine(Background, Foreground, Dark, Palette.Count);
}