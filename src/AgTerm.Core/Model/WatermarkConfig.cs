namespace AgTerm.Core.Model;

/// <summary>
/// Host-free validation for BackgroundWatermark fields, shared by the CLI and the control server.
/// The upstream overlayText emitter (the ghostty config-overlay text) is rendering-engine specific
/// and is NOT part of the control-API parity surface.
/// </summary>
public static class WatermarkConfig
{
    /// <summary>The valid fit tokens, derived from the WatermarkFit enum (single source of truth) —
    /// used for the CLI/server error messages.</summary>
    public static readonly IReadOnlyList<string> ValidFits = new[] { "contain", "cover", "stretch", "none" };

    /// <summary>The valid position tokens, derived from WatermarkPosition.</summary>
    public static readonly IReadOnlyList<string> ValidPositions = new[]
    {
        "top-left", "top-center", "top-right",
        "center-left", "center", "center-right",
        "bottom-left", "bottom-center", "bottom-right",
    };

    /// <summary>
    /// Upper bound on watermark text length accepted at the control boundary: the renderer rasterizes
    /// the string with a bitmap sized to the glyph run, so an uncapped string would drive a huge
    /// allocation. A watermark is a word or two; 256 is far beyond any real use.
    /// </summary>
    public const int MaxTextLength = 256;

    public static bool IsValidFit(string fit) => WatermarkFits.FromWire(fit) is not null;

    public static bool IsValidPosition(string position) => WatermarkPositions.FromWire(position) is not null;

    /// <summary>Valid opacity: a finite value in 0...1 (the documented range). Rejects NaN/Inf and
    /// out-of-range values.</summary>
    public static bool IsValidOpacity(double opacity) =>
        double.IsFinite(opacity) && opacity >= 0 && opacity <= 1;

    /// <summary>Valid watermark text: non-empty and within MaxTextLength (measured in grapheme
    /// clusters, matching Swift's String.count), so the rasterized bitmap stays bounded.</summary>
    public static bool IsValidText(string text) =>
        text.Length != 0 && TextElementCount(text) <= MaxTextLength;

    /// <summary>
    /// Valid #rrggbb hex color: an optional leading # then exactly six ASCII hex digits. A malformed
    /// value is rejected at the boundary rather than silently falling back to the terminal foreground.
    /// The digit check is ASCII-only: fullwidth Unicode hex forms would pass a naive check and then
    /// silently render the default color.
    /// </summary>
    public static bool IsValidColorHex(string hex)
    {
        var span = hex.AsSpan();
        if (span.Length > 0 && span[0] == '#') span = span[1..];
        if (span.Length != 6) return false;
        foreach (var c in span)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }
        return true;
    }

    /// <summary>
    /// Valid image watermark path: no ASCII control character (any scalar &lt; 0x20). The path is the
    /// only free-text field that reaches an engine config line raw, so an embedded newline could
    /// inject an arbitrary config key; the injection vector is closed at the boundary.
    /// </summary>
    public static bool IsValidImagePath(string path) => !path.Any(c => c < 0x20);

    /// <summary>Grapheme-cluster count — the C# analogue of Swift's String.count.</summary>
    public static int TextElementCount(string text) =>
        new System.Globalization.StringInfo(text).LengthInTextElements;
}
