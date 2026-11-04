namespace AgTerm.Core.Model;

/// <summary>The physical arrangement of a session's two panes. The wire names are the divider
/// direction: a vertical divider gives left/right panes, a horizontal one gives top/bottom.</summary>
public enum SplitAxis
{
    LeftRight,
    TopBottom,
}

public static class SplitAxes
{
    public static string Wire(SplitAxis axis) => axis == SplitAxis.TopBottom ? "horizontal" : "vertical";

    public static bool TryParse(string? name, out SplitAxis axis)
    {
        switch (name)
        {
            case "vertical": axis = SplitAxis.LeftRight; return true;
            case "horizontal": axis = SplitAxis.TopBottom; return true;
            default: axis = SplitAxis.LeftRight; return false;
        }
    }
}
