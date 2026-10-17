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

    /// <summary>The window's name when the user set it, or null for an auto "window N" name.</summary>
    public static string? CustomWindowName(string windowName) =>
        WindowNames.IsAutoName(windowName) ? null : windowName;

    public static string Visible(string? session, string? customWindowName, AppSettings settings) =>
        Join(
            settings.IsInterfaceElementHidden(InterfaceElement.SessionName) ? null : session,
            settings.IsInterfaceElementHidden(InterfaceElement.WindowName) ? null : customWindowName);

    public static string OsTitle(string? session, string? customWindowName) =>
        ProductIdentity.WindowTitle(Join(session, customWindowName));

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
