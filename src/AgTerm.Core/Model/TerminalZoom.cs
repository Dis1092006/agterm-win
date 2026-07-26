using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>A session's four addressable terminal surfaces. Wire raw values: left, right, scratch,
/// overlay (the primary/split panes report their user-facing side names).</summary>
public enum TerminalZoomSurface { Primary, Split, Scratch, Overlay }

public static class TerminalZoomSurfaces
{
    /// <summary>Declaration order, mirroring Swift CaseIterable.allCases.</summary>
    public static readonly IReadOnlyList<TerminalZoomSurface> AllCases = new[]
    {
        TerminalZoomSurface.Primary, TerminalZoomSurface.Split,
        TerminalZoomSurface.Scratch, TerminalZoomSurface.Overlay,
    };

    /// <summary>The Swift rawValue.</summary>
    public static string WireName(this TerminalZoomSurface surface) => surface switch
    {
        TerminalZoomSurface.Primary => "left",
        TerminalZoomSurface.Split => "right",
        TerminalZoomSurface.Scratch => "scratch",
        TerminalZoomSurface.Overlay => "overlay",
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>The control-name parse, accepting both the side names and the role aliases.</summary>
    public static TerminalZoomSurface? FromControlName(string controlName) => controlName switch
    {
        "left" or "primary" => TerminalZoomSurface.Primary,
        "right" or "split" => TerminalZoomSurface.Split,
        "scratch" => TerminalZoomSurface.Scratch,
        "overlay" => TerminalZoomSurface.Overlay,
        _ => null,
    };

    /// <summary>Whether the surface slot is addressable in this session (hidden-but-alive included).</summary>
    public static bool IsAvailable(this TerminalZoomSurface surface, Session session) => surface switch
    {
        TerminalZoomSurface.Primary =>
            !(session.Surface is null && session.SplitSurface is not null && session.SplitFocused),
        TerminalZoomSurface.Split => session.HasSplit || session.SplitSurface is not null,
        TerminalZoomSurface.Scratch => session.ScratchActive || session.ScratchSurface is not null,
        TerminalZoomSurface.Overlay => session.OverlayActive,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Whether the surface is the one holding keyboard focus. The predicates are mutually
    /// exclusive per case.</summary>
    public static bool IsActive(this TerminalZoomSurface surface, Session session) => surface switch
    {
        TerminalZoomSurface.Primary =>
            !session.OverlayActive && !session.ScratchActive && !session.SplitFocused,
        TerminalZoomSurface.Split =>
            !session.OverlayActive && !session.ScratchActive && session.SplitFocused,
        TerminalZoomSurface.Scratch => !session.OverlayActive && session.ScratchActive,
        TerminalZoomSurface.Overlay => session.OverlayActive,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Whether the surface is currently rendered on screen.</summary>
    public static bool IsVisible(this TerminalZoomSurface surface, Session session) => surface switch
    {
        TerminalZoomSurface.Primary =>
            !session.OverlayActive && !session.ScratchActive && (!session.SplitFocused || session.IsSplit),
        TerminalZoomSurface.Split =>
            !session.OverlayActive && !session.ScratchActive && (session.IsSplit || session.SplitFocused),
        TerminalZoomSurface.Scratch => !session.OverlayActive && session.ScratchActive,
        TerminalZoomSurface.Overlay => session.OverlayActive,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };
}

/// <summary>The stable control address of one surface: surface:&lt;SESSION-UUID&gt;:&lt;kind&gt;.</summary>
public readonly record struct TerminalSurfaceId(Guid SessionId, TerminalZoomSurface Surface)
{
    public string RawValue =>
        $"surface:{SessionId.ToString("D").ToUpperInvariant()}:{Surface.WireName()}";

    public override string ToString() => RawValue;

    /// <summary>Parses the surface:&lt;uuid&gt;:&lt;kind&gt; form; null for anything else.</summary>
    public static TerminalSurfaceId? FromRawValue(string rawValue)
    {
        var parts = rawValue.Split(':');
        if (parts.Length != 3 || parts[0] != "surface") return null;
        if (!Guid.TryParseExact(parts[1], "D", out var sessionId)) return null;
        var surface = TerminalZoomSurfaces.FromControlName(parts[2]);
        if (surface is null) return null;
        return new TerminalSurfaceId(sessionId, surface.Value);
    }
}

/// <summary>The zoom target: one session surface, or the quick terminal.</summary>
public abstract record TerminalZoomTarget
{
    private TerminalZoomTarget()
    {
    }

    public sealed record Session(Guid SessionId, TerminalZoomSurface Surface) : TerminalZoomTarget;

    public sealed record Quick : TerminalZoomTarget;

    public string ControlId => this switch
    {
        Session s => new TerminalSurfaceId(s.SessionId, s.Surface).RawValue,
        Quick => "quick",
        _ => throw new InvalidOperationException(),
    };
}

/// <summary>Per-window zoom state: which surface (if any) is maximized over the whole window.</summary>
public sealed class TerminalZoomController
{
    public TerminalZoomTarget? Target { get; private set; }

    public Func<TerminalZoomTarget?>? TargetResolver { get; set; }

    public void Toggle()
    {
        Target = Target is not null ? null : TargetResolver?.Invoke();
    }

    public void Set(ControlToggleMode mode, TerminalZoomTarget? newTarget)
    {
        switch (mode)
        {
            case ControlToggleMode.On:
                if (newTarget is not null) Target = newTarget;
                break;
            case ControlToggleMode.Off:
                if (newTarget is not null)
                {
                    if (Target == newTarget) Target = null;
                }
                else
                {
                    Target = null;
                }
                break;
            case ControlToggleMode.Toggle:
                if (newTarget is null)
                {
                    Target = null;
                    return;
                }
                Target = Target == newTarget ? null : newTarget;
                break;
        }
    }

    public void Clear() => Target = null;

    /// <summary>The zoom target the toggle resolves for the current state: the quick terminal when
    /// visible, else the active session's active surface.</summary>
    public static TerminalZoomTarget? ResolveTarget(AppStore store, bool quickTerminalVisible)
    {
        if (quickTerminalVisible) return new TerminalZoomTarget.Quick();
        if (store.ActiveSession is not { } session) return null;
        var surface = TerminalZoomSurfaces.AllCases.FirstOrDefault(s => s.IsActive(session),
            TerminalZoomSurface.Primary);
        return new TerminalZoomTarget.Session(session.Id, surface);
    }

    public static bool IsTargetValid(TerminalZoomTarget target, AppStore store, bool quickTerminalVisible)
    {
        switch (target)
        {
            case TerminalZoomTarget.Quick:
                return quickTerminalVisible;
            case TerminalZoomTarget.Session s:
                var session = store.SessionWithId(s.SessionId);
                return session is not null && s.Surface.IsAvailable(session);
            default:
                return false;
        }
    }
}
