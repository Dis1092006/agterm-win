namespace AgTerm.Core.Model;

/// <summary>
/// ReorderDirection is a relative one-step reorder used by the control API/CLI (--to),
/// mirroring session.go --to next|prev|first|last. No wraparound.
/// </summary>
public enum ReorderDirection { Up, Down, Top, Bottom }

public static class ReorderDirections
{
    /// <summary>The Swift raw-value parse: the wire token, or null for an unknown string.</summary>
    public static ReorderDirection? FromWire(string wire) => wire switch
    {
        "up" => ReorderDirection.Up,
        "down" => ReorderDirection.Down,
        "top" => ReorderDirection.Top,
        "bottom" => ReorderDirection.Bottom,
        _ => null,
    };

    /// <summary>
    /// The post-removal insert index for a relative reorder within a list of count elements, or null
    /// when the move is a no-op (already at the end in this direction). No wraparound.
    /// </summary>
    public static int? DestinationIndex(this ReorderDirection direction, int current, int count) => direction switch
    {
        ReorderDirection.Up => current > 0 ? current - 1 : null,
        ReorderDirection.Down => current < count - 1 ? current + 1 : null,
        ReorderDirection.Top => current > 0 ? 0 : null,
        ReorderDirection.Bottom => current < count - 1 ? count - 1 : null,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
