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

    /// <summary>A web page, kept to its origin by the navigation policy. Address keeps the text it
    /// was given in OriginalString, which the tree reports.</summary>
    public sealed record Url(Uri Address) : HtmlSource;

    /// <summary>Parses --url: an absolute http or https URL with a host, null for anything else
    /// (upstream HtmlSource.webURL).</summary>
    public static Uri? WebUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) && Origin(url) is not null ? url : null;

    /// <summary>Whether two URLs have the same web origin: scheme, host and effective port.</summary>
    public static bool SameOrigin(Uri left, Uri right) =>
        Origin(left) is { } origin && origin == Origin(right);

    /// <summary>The web origin of an http or https URL as a user compares it: the lowercase scheme
    /// and host, and the port only when it is not the scheme's default; null for any other URL.</summary>
    public static string? Origin(Uri url)
    {
        if (!url.IsAbsoluteUri) return null;
        var scheme = url.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https") || string.IsNullOrEmpty(url.Host)) return null;
        var host = url.Host.ToLowerInvariant();
        if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";
        return url.IsDefaultPort ? $"{scheme}://{host}" : $"{scheme}://{host}:{url.Port}";
    }
}

/// <summary>What the web view shows now: the page address, its title and its history.</summary>
public sealed record HtmlPageInfo(string Page, string? Title, bool CanGoBack, bool CanGoForward);

/// <summary>A page in an overlay slot (upstream HtmlOverlay). The store owns it; the app's web view
/// reports into it by Id. Never persisted.</summary>
public sealed class HtmlOverlay(HtmlSource source, Guid? id = null)
{
    /// <summary>The identity strip's height in points; ChromeHeight adds the rule under it.</summary>
    public const double StripHeight = 28;
    public const double ChromeHeight = StripHeight + 1;

    public Guid Id { get; } = id ?? Guid.NewGuid();

    public HtmlSource Source { get; } = source;

    /// <summary>Whether the page shows the back, forward, reload, browser and reveal buttons.</summary>
    public bool Navigation { get; set; }

    /// <summary>Whether the page may run its own scripts.</summary>
    public bool Javascript { get; set; }

    public bool Chromeless { get; init; }

    /// <summary>What the panel draws above the page: the strip and its rule, none for a chromeless page.</summary>
    public double Chrome => Chromeless ? 0 : ChromeHeight;

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

    /// <summary>A separate page with the same id and values, for code that reads the page off the
    /// thread that changes it.</summary>
    public HtmlOverlay Copy() => new(Source, Id)
    {
        Navigation = Navigation,
        Javascript = Javascript,
        Chromeless = Chromeless,
        Persistent = Persistent,
        Browse = Browse,
        LoadState = LoadState,
        LoadError = LoadError,
        Current = Current,
        ReloadRevision = ReloadRevision,
        ReloadTarget = ReloadTarget,
    };

    /// <summary>Names what the page shows from what the app loaded, never from the page's title,
    /// which the page writes and could dress as a prompt: the file shown, the origin a URL page is
    /// kept to, or for a browsing page the origin of the document it shows now.</summary>
    public string Identity => IdentityAt(Current);

    /// <summary>Identity for current, the page a live web view reports.</summary>
    public string IdentityAt(HtmlPageInfo? current)
    {
        if (Source is not HtmlSource.Url url) return System.IO.Path.GetFileName(PageAt(current));
        var shown = Browse && current is not null
            ? (Uri.TryCreate(current.Page, UriKind.Absolute, out var page) ? HtmlSource.Origin(page) : null) ?? current.Page
            : null;
        return shown ?? HtmlSource.Origin(url.Address) ?? url.Address.OriginalString;
    }

    /// <summary>The full path or address of current, or the source before the first report.</summary>
    public string PageAt(HtmlPageInfo? current) => Source switch
    {
        HtmlSource.File file => current?.Page ?? file.Path,
        HtmlSource.Url url => current?.Page ?? url.Address.OriginalString,
        _ => "",
    };

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
