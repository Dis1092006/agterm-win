namespace AgTerm.Core.Model;

/// <summary>The pane a pane-scoped ask covers. Two cases rather than StatusPane, so a scratch
/// anchor cannot be represented (upstream OverlayPane).</summary>
public enum OverlayPane
{
    Left,
    Right,
}

public static class OverlayPanes
{
    /// <summary>Accepts the role and axis aliases upstream accepts; the rejection message names
    /// only left and right.</summary>
    public static OverlayPane? FromControlName(string name) => name switch
    {
        "left" or "top" or "primary" => OverlayPane.Left,
        "right" or "bottom" or "split" => OverlayPane.Right,
        _ => null,
    };

    public static string Wire(OverlayPane pane) => pane == OverlayPane.Left ? "left" : "right";
}
