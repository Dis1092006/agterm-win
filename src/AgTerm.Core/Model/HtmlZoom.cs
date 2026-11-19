using System.Globalization;

namespace AgTerm.Core.Model;

/// <summary>One page zoom shared by every HTML overlay, stepped by the font actions (upstream
/// HtmlZoom, #670).</summary>
public static class HtmlZoom
{
    public static IReadOnlyList<double> Steps { get; } = [0.5, 0.75, 0.85, 1, 1.15, 1.25, 1.5, 1.75, 2, 2.5, 3];

    private const double Smallest = 0.5, Largest = 3.0;

    /// <summary>Bounds a stored factor to the ladder's range; null and a non-finite value mean actual
    /// size.</summary>
    public static double Resolve(double? stored) =>
        stored is { } value && double.IsFinite(value) ? Math.Clamp(value, Smallest, Largest) : 1;

    /// <summary>The next factor from zoom for a font binding action (increase_font_size:1,
    /// decrease_font_size:1, reset_font_size); null for any other action. An off-ladder factor steps
    /// to its neighbor.</summary>
    public static double? Applying(string action, double zoom) => action.Split(':')[0] switch
    {
        "increase_font_size" => Steps.Where(step => step > zoom).DefaultIfEmpty(Largest).First(),
        "decrease_font_size" => Steps.Where(step => step < zoom).DefaultIfEmpty(Smallest).Last(),
        "reset_font_size" => 1,
        _ => null,
    };

    /// <summary>Sets the zoom of the top document through one constructed sheet it keeps on the
    /// document, so running it again replaces the factor. Upstream sets the web view's pageZoom; the
    /// WinUI web view has no zoom of its own, so the port zooms the root element with CSS. A subframe
    /// is left alone, since it already scales with the top document.</summary>
    public static string Script(double zoom)
    {
        var rule = zoom == 1 ? "" : $":root {{ zoom: {zoom.ToString(CultureInfo.InvariantCulture)}; }}";
        return "(() => {\n" +
               "  if (window !== window.top) return;\n" +
               "  const key = Symbol.for('agterm-zoom');\n" +
               "  let sheet = document[key];\n" +
               "  if (!sheet) { sheet = new CSSStyleSheet(); document[key] = sheet; }\n" +
               "  if (!document.adoptedStyleSheets.includes(sheet))\n" +
               "    document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];\n" +
               $"  sheet.replaceSync('{rule}');\n" +
               "})();";
    }
}
