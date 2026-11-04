using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// The window's title text — upstream WindowContentView titleText and windowTitle. The title bar shows
/// the session name and the window's custom name as "session — window", each part hidden by its
/// interface element. The OS window title is never gated, so Alt-Tab and the taskbar stay labelled.
/// </summary>
public static class TitleBarText
{
    private const string Separator = " — ";
    private const string ContextSeparator = " · ";

    /// <summary>The window's name when the user set it, or null for an auto "window N" name.</summary>
    public static string? CustomWindowName(string windowName) =>
        WindowNames.IsAutoName(windowName) ? null : windowName;

    public static string Visible(string? session, string? customWindowName, AppSettings settings) =>
        Visible(session, customWindowName, null, settings);

    /// <summary>The title bar's line one. A compact bar has one row, so the context trails the identity
    /// after " · " (upstream TitlebarComposition); the normal bar shows it on line two instead.</summary>
    public static string Visible(string? session, string? customWindowName, string? context, AppSettings settings)
    {
        var identity = Join(
            settings.IsInterfaceElementHidden(InterfaceElement.SessionName) ? null : session,
            settings.IsInterfaceElementHidden(InterfaceElement.WindowName) ? null : customWindowName);
        if (settings.EffectiveToolbarMode() != ToolbarMode.Compact
            || settings.IsInterfaceElementHidden(InterfaceElement.SessionContext)
            || string.IsNullOrEmpty(context))
            return identity;
        return identity.Length == 0 ? context : identity + ContextSeparator + context;
    }

    public static string OsTitle(string? session, string? customWindowName) =>
        ProductIdentity.WindowTitle(Join(session, customWindowName));

    // upstream dashboardWindowTitle: not gated by the interface elements, no session part
    public static string Dashboard(string? customWindowName) => Join("Dashboard", customWindowName);

    public static double ToolbarHeight(AppSettings settings) =>
        settings.EffectiveToolbarMode() == ToolbarMode.Normal ? 48 : 32;

    public static string Subtitle(string? detail, string? context, AppSettings settings)
    {
        if (settings.EffectiveToolbarMode() != ToolbarMode.Normal)
            return string.Empty;
        var shown = settings.IsInterfaceElementHidden(InterfaceElement.SessionContext) ? null : context;
        return string.IsNullOrEmpty(shown) ? detail ?? string.Empty : shown;
    }

    // an empty part (a split or zoom without a title) counts as absent
    private static string Join(string? session, string? window) =>
        (NullIfEmpty(session), NullIfEmpty(window)) switch
    {
        ({ } s, { } w) => s + Separator + w,
        ({ } s, null) => s,
        (null, { } w) => w,
        _ => string.Empty,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
