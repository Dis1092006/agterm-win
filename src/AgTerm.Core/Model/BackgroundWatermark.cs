using System.Text.Json.Serialization;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>
/// A per-session background composited behind the terminal grid: a user-supplied image file
/// (<see cref="WatermarkKind.Image"/>), a string rasterized to an image (<see cref="WatermarkKind.Text"/>),
/// or a solid #rrggbb background color (<see cref="WatermarkKind.Color"/>). Stored on the session,
/// persisted in the session snapshot (LOSSY decode there), and carried on the control wire as the
/// tree node's <c>background</c> field.
/// </summary>
public sealed record BackgroundWatermark
{
    [JsonPropertyName("kind")]
    public required WatermarkKind Kind { get; init; }

    /// <summary>For Image: the user-supplied image file path. Null for Text (the image is generated).</summary>
    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; init; }

    /// <summary>For Text: the watermark string.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// For Text: the text color as #rrggbb; null = the terminal foreground at render time.
    /// For Color: the solid background color as #rrggbb (required).
    /// </summary>
    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; init; }

    /// <summary>Image opacity (relative to the background opacity); null = the engine's 1.0 default.</summary>
    [JsonPropertyName("opacity")]
    public double? Opacity { get; init; }

    /// <summary>Null = contain.</summary>
    [JsonPropertyName("fit")]
    public WatermarkFit? Fit { get; init; }

    /// <summary>Null = center.</summary>
    [JsonPropertyName("position")]
    public WatermarkPosition? Position { get; init; }

    /// <summary>Tile the image to fill blank space. Null = false.</summary>
    [JsonPropertyName("repeats")]
    public bool? Repeats { get; init; }
}

[JsonConverter(typeof(WatermarkKindConverter))]
public enum WatermarkKind { Image, Text, Color }

public sealed class WatermarkKindConverter : WireEnumConverter<WatermarkKind>
{
    public WatermarkKindConverter() : base(
        (WatermarkKind.Image, "image"), (WatermarkKind.Text, "text"), (WatermarkKind.Color, "color"))
    {
    }
}

/// <summary>background-image-fit; raw values match the engine's keys exactly.</summary>
[JsonConverter(typeof(WatermarkFitConverter))]
public enum WatermarkFit { Contain, Cover, Stretch, None }

public static class WatermarkFits
{
    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static WatermarkFit? FromWire(string wire) => wire switch
    {
        "contain" => WatermarkFit.Contain,
        "cover" => WatermarkFit.Cover,
        "stretch" => WatermarkFit.Stretch,
        "none" => WatermarkFit.None,
        _ => null,
    };
}

public sealed class WatermarkFitConverter : WireEnumConverter<WatermarkFit>
{
    public WatermarkFitConverter() : base(
        (WatermarkFit.Contain, "contain"), (WatermarkFit.Cover, "cover"),
        (WatermarkFit.Stretch, "stretch"), (WatermarkFit.None, "none"))
    {
    }
}

/// <summary>background-image-position: center plus the 8 edge/corner anchors.</summary>
[JsonConverter(typeof(WatermarkPositionConverter))]
public enum WatermarkPosition
{
    TopLeft, TopCenter, TopRight,
    CenterLeft, Center, CenterRight,
    BottomLeft, BottomCenter, BottomRight,
}

public static class WatermarkPositions
{
    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static WatermarkPosition? FromWire(string wire) => wire switch
    {
        "top-left" => WatermarkPosition.TopLeft,
        "top-center" => WatermarkPosition.TopCenter,
        "top-right" => WatermarkPosition.TopRight,
        "center-left" => WatermarkPosition.CenterLeft,
        "center" => WatermarkPosition.Center,
        "center-right" => WatermarkPosition.CenterRight,
        "bottom-left" => WatermarkPosition.BottomLeft,
        "bottom-center" => WatermarkPosition.BottomCenter,
        "bottom-right" => WatermarkPosition.BottomRight,
        _ => null,
    };
}

public sealed class WatermarkPositionConverter : WireEnumConverter<WatermarkPosition>
{
    public WatermarkPositionConverter() : base(
        (WatermarkPosition.TopLeft, "top-left"), (WatermarkPosition.TopCenter, "top-center"),
        (WatermarkPosition.TopRight, "top-right"), (WatermarkPosition.CenterLeft, "center-left"),
        (WatermarkPosition.Center, "center"), (WatermarkPosition.CenterRight, "center-right"),
        (WatermarkPosition.BottomLeft, "bottom-left"), (WatermarkPosition.BottomCenter, "bottom-center"),
        (WatermarkPosition.BottomRight, "bottom-right"))
    {
    }
}
