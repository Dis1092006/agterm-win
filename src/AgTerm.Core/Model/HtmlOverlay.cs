namespace AgTerm.Core.Model;

/// <summary>Where a page stands in loading (upstream HtmlLoadState). The wire value is the
/// lowercase name.</summary>
public enum HtmlLoadState { Loading, Loaded, Failed }

/// <summary>What session.overlay.navigate asks of a page (upstream HtmlNavigation).</summary>
public enum HtmlNavigation { Back, Forward, Browser, Finder }

/// <summary>What a reload loads: the source the page was opened with, or the page shown now.</summary>
public enum HtmlReloadTarget { Original, Current }

public static class HtmlNavigations
{
    public static HtmlNavigation? Parse(string? raw) => raw switch
    {
        "back" => HtmlNavigation.Back,
        "forward" => HtmlNavigation.Forward,
        "browser" => HtmlNavigation.Browser,
        "finder" => HtmlNavigation.Finder,
        _ => null,
    };

    public static string Wire(this HtmlNavigation navigation) => navigation switch
    {
        HtmlNavigation.Back => "back",
        HtmlNavigation.Forward => "forward",
        HtmlNavigation.Browser => "browser",
        HtmlNavigation.Finder => "finder",
        _ => throw new ArgumentOutOfRangeException(nameof(navigation)),
    };
}

/// <summary>What a page shows (upstream HtmlSource).</summary>
public abstract record HtmlSource
{
    private HtmlSource() { }

    /// <summary>A local HTML file. GrantRoot is the directory the page may read, or null for no
    /// file access beyond the file itself.</summary>
    public sealed record File(string Path, string? GrantRoot) : HtmlSource;
}

/// <summary>What the web view shows now: the page address, its title and its history.</summary>
public sealed record HtmlPageInfo(string Page, string? Title, bool CanGoBack, bool CanGoForward);

/// <summary>A page in an overlay slot (upstream HtmlOverlay). The store owns it; the app's web view
/// reports into it by Id. Never persisted.</summary>
public sealed class HtmlOverlay(HtmlSource source, Guid? id = null)
{
    public Guid Id { get; } = id ?? Guid.NewGuid();

    public HtmlSource Source { get; } = source;

    /// <summary>Whether the page shows the back, forward, reload, browser and reveal buttons.</summary>
    public bool Navigation { get; set; }

    /// <summary>Whether the page may run its own scripts.</summary>
    public bool Javascript { get; set; }

    public bool Chromeless { get; init; }

    public bool Persistent { get; init; }

    public bool Browse { get; init; }

    public HtmlLoadState LoadState { get; set; } = HtmlLoadState.Loading;

    public string? LoadError { get; set; }

    /// <summary>What the web view last reported showing; null before it reports and after a reload
    /// of the original source.</summary>
    public HtmlPageInfo? Current { get; set; }

    /// <summary>Moves on every reload request, so the app can tell a new request from one it served.</summary>
    public int ReloadRevision { get; set; }

    public HtmlReloadTarget ReloadTarget { get; set; } = HtmlReloadTarget.Original;

    /// <summary>Why a file page with grant root grantRoot cannot open, or null when it can
    /// (upstream HtmlOverlay.grantError). On Windows a drive or share root counts as "/", home is the
    /// user profile, and components compare case-insensitively.</summary>
    public static string? GrantError(string file, string? grantRoot, string home)
    {
        if (!System.IO.Path.IsPathFullyQualified(file)) return "html file must be an absolute path";
        if (grantRoot is null) return null;
        if (!System.IO.Path.IsPathFullyQualified(grantRoot)) return "cwd must be an absolute path";
        var grant = Components(grantRoot);
        if (IsRoot(grantRoot) || SameComponents(grant, Components(home)))
            return "cwd must not be / or the home directory";
        if (SameComponents(grant, Components(file))) return "cwd must be a directory containing the html file";
        if (!Contains(grantRoot, file)) return "html file is outside cwd";
        return null;
    }

    /// <summary>Whether path lies at or under root, comparing whole standardized components.</summary>
    public static bool Contains(string root, string path)
    {
        var rootParts = Components(root);
        var pathParts = Components(path);
        return rootParts.Length <= pathParts.Length
            && SameComponents(rootParts, pathParts[..rootParts.Length]);
    }

    private static bool IsRoot(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full) ?? "";
        return string.Equals(Trim(full), Trim(root), StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string path) =>
        path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

    private static string[] Components(string path) =>
        System.IO.Path.GetFullPath(path).Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
                                               StringSplitOptions.RemoveEmptyEntries);

    private static bool SameComponents(string[] a, string[] b) =>
        a.Length == b.Length && a.Zip(b).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Why AppStore.OpenHtmlOverlay refused.</summary>
public enum HtmlOverlayOpenFailure { UnknownSession, AlreadyOpen }

/// <summary>Why a command addressing a page (reload, navigate) refused.</summary>
public enum HtmlOverlayCommandFailure { UnknownSession, NoOverlay, NotHtml }

public static class HtmlOverlayFailures
{
    public static string Message(this HtmlOverlayOpenFailure failure) => failure switch
    {
        HtmlOverlayOpenFailure.UnknownSession => "no such session",
        HtmlOverlayOpenFailure.AlreadyOpen => "overlay already open",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    public static string Message(this HtmlOverlayCommandFailure failure) => failure switch
    {
        HtmlOverlayCommandFailure.UnknownSession => "no such session",
        HtmlOverlayCommandFailure.NoOverlay => AgTerm.Core.Protocol.OverlayHtmlError.NoOverlay,
        HtmlOverlayCommandFailure.NotHtml => AgTerm.Core.Protocol.OverlayHtmlError.NotHtml,
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
