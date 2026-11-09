using AgTerm.Core.Model;
using System.Globalization;

namespace AgTerm.Xterm;

/// <summary>
/// The window's title text — upstream WindowContentView titleText and windowTitle. The title bar shows
/// the active session's workspace, the session name and the window's custom name as
/// "workspace — session — window", each part gated by its interface element. The OS window title is
/// never gated and carries no workspace, so Alt-Tab and the taskbar stay labelled.
/// </summary>
public static class TitleBarText
{
    private const string Separator = " — ";
    private const string ContextSeparator = " · ";

    /// <summary>Characters of the workspace name kept before the ellipsis. Upstream
    /// TitlebarComposition.workspaceNameLimit.</summary>
    public const int WorkspaceNameLimit = 24;

    /// <summary>The window's name when the user set it, or null for an auto "window N" name.</summary>
    public static string? CustomWindowName(string windowName) =>
        WindowNames.IsAutoName(windowName) ? null : windowName;

    public static string Visible(string? session, string? customWindowName, AppSettings settings) =>
        Visible(session, customWindowName, null, settings);

    /// <summary>The title bar's line one. A compact bar has one row, so the context trails the identity
    /// after " · " (upstream TitlebarComposition); the normal bar shows it on line two instead.</summary>
    public static string Visible(string? session, string? customWindowName, string? context, AppSettings settings)
        => Visible(null, session, customWindowName, context, settings);

    public static string Visible(string? workspace, string? session, string? customWindowName, string? context,
        AppSettings settings)
    {
        var identity = Join(
            settings.IsInterfaceElementHidden(InterfaceElement.WorkspaceName) ? null : CappedWorkspaceName(workspace),
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

    // upstream TitlebarComposition.cappedWorkspaceName: trimmed, blank dropped, and cut to the limit in
    // characters (text elements, so a composed character stays whole) with an ellipsis
    private static string? CappedWorkspaceName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        var info = new StringInfo(trimmed);
        return info.LengthInTextElements <= WorkspaceNameLimit
            ? trimmed
            : info.SubstringByTextElements(0, WorkspaceNameLimit) + "…";
    }

    // an empty part (a split or zoom without a title) counts as absent
    private static string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(part => !string.IsNullOrEmpty(part)));

}
