namespace AgTerm.Core.Model;

/// <summary>
/// How the dashboard overlay sizes the font of its member surfaces. Untouched leaves each surface at
/// its own session fontSize; Fixed applies one absolute size to every cell; Auto derives a per-grid
/// size so a denser grid shrinks to stay readable. Mirrors the Swift enum with associated value.
/// </summary>
public abstract record DashboardFontMode
{
    private DashboardFontMode()
    {
    }

    public sealed record Untouched : DashboardFontMode;

    public sealed record Fixed(double Value) : DashboardFontMode;

    public sealed record Auto : DashboardFontMode;
}
