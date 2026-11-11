namespace AgTerm.Core.Model;

/// <summary>Overrides a floating overlay's size percent per axis, in points of the area its program
/// or page gets; a page's strip comes on top (upstream OverlayPointSize). A null axis keeps the
/// percent.</summary>
public readonly record struct OverlayPointSize(int? Width = null, int? Height = null)
{
    public bool IsEmpty => Width is null && Height is null;

    /// <summary>The most of the session a point axis may take, the HUD's cap, so a panel sized in
    /// points always leaves some of the session in view. A percent axis keeps its 1...100.</summary>
    public const double MaxShare = HudLayout.MaxSizePercent / 100.0;

    /// <summary>These sizes with every axis other sets replaced.</summary>
    public OverlayPointSize Merging(OverlayPointSize other) => new(other.Width ?? Width, other.Height ?? Height);

    /// <summary>The floating panel's outer size inside bounds, chrome being the strip a page draws
    /// above its content. A point axis is capped after the chrome is added, so the strip cannot push
    /// the panel past MaxShare.</summary>
    public static (double Width, double Height) PanelSize((double Width, double Height) bounds, int sizePercent,
                                                          OverlayPointSize points, double chrome = 0)
    {
        var share = sizePercent / 100.0;
        var width = points.Width is { } w ? Math.Min(w, bounds.Width * MaxShare) : bounds.Width * share;
        var height = points.Height is { } h ? Math.Min(h + chrome, bounds.Height * MaxShare) : bounds.Height * share;
        return (width, height);
    }

    /// <summary>What PanelSize leaves the program or page once the chrome is taken.</summary>
    public static (double Width, double Height) ContentSize((double Width, double Height) bounds, int sizePercent,
                                                            OverlayPointSize points, double chrome)
    {
        var panel = PanelSize(bounds, sizePercent, points, chrome);
        return (panel.Width, Math.Max(0, panel.Height - chrome));
    }
}

/// <summary>Why AppStore.ResizeOverlayInPoints refused point sizes. Upstream also has a remote case;
/// the port has no remote overlays.</summary>
public enum OverlayPointResizeFailure
{
    NoOverlay,
    /// <summary>A HUD measures its own height.</summary>
    Hud,
    /// <summary>Points alone need a floating panel whose percent the other axis keeps.</summary>
    NotFloating,
}
