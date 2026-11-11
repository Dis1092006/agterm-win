using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>A navigation the web view is about to make. Setting Cancel stops it; a new window
/// never opens whatever Cancel says. Url is the raw string: the data: URL of a text load carries
/// the whole page, longer than System.Uri takes.</summary>
public sealed class HtmlPageNavigationStarting(string url, HtmlNavigationTarget target, bool userInitiated)
{
    public string Url { get; } = url;

    public HtmlNavigationTarget Target { get; } = target;

    /// <summary>Whether the user's input started it. WebView2 counts a host Navigate and
    /// script the host runs as the user's.</summary>
    public bool UserInitiated { get; } = userInitiated;

    public bool Cancel { get; set; }
}

/// <summary>How a main-frame navigation ended. Canceled is a navigation stopped by the policy or
/// superseded by the next one; Error names the failure otherwise.</summary>
public sealed record HtmlPageNavigationCompleted(bool IsSuccess, bool Canceled, string? Error);

/// <summary>
/// The web view of one page in the overlay slot. Two adapters exist for the same reason as
/// IWebViewScriptHost: the WinRT and Win32 CoreWebView2 types differ. All members are
/// thread-affine to the web view's UI thread.
/// </summary>
public interface IHtmlPageView
{
    /// <summary>Applies the page's settings before its first load: page scripts on only with
    /// javascript; script dialogs, dev tools, host objects, web messages, autofill and the status
    /// bar off; permission requests denied and downloads cancelled.</summary>
    void Configure(bool javascript);

    /// <summary>Serves folder as https://host/ for this web view only.</summary>
    void MapVirtualHost(string host, string folder);

    /// <summary>Replaces the script that runs at the creation of every later document, before
    /// the page's own scripts and whether or not page scripts are on.</summary>
    Task SetDocumentScriptAsync(string script);

    void Navigate(string url);

    void NavigateToString(string html);

    void Reload();

    void GoBack();

    void GoForward();

    void Stop();

    /// <summary>Releases the web view for good.</summary>
    void Close();

    bool CanGoBack { get; }

    bool CanGoForward { get; }

    /// <summary>The address shown; about:blank for a page loaded from text.</summary>
    string? Source { get; }

    string? DocumentTitle { get; }

    /// <summary>Main frame, subframe and new-window navigations, before they start.</summary>
    event Action<HtmlPageNavigationStarting>? NavigationStarting;

    /// <summary>A main-frame navigation committed its document.</summary>
    event Action? ContentLoading;

    event Action<HtmlPageNavigationCompleted>? NavigationCompleted;

    /// <summary>The source, the title or the history changed.</summary>
    event Action? PageChanged;

    /// <summary>The process showing the page went away.</summary>
    event Action? ProcessFailed;

    /// <summary>Real keyboard or mouse input reached the page; script cannot raise it.</summary>
    event Action? UserInput;
}

/// <summary>Hands a page's URLs to the user's web browser and its files to Explorer (upstream
/// HtmlBrowser and HtmlSharing).</summary>
public interface IHtmlBrowser
{
    /// <summary>Puts a nonblocking prompt up, calls done once with the answer, and returns what
    /// dismisses it, or null when it could show nothing.</summary>
    Action? Confirm(Uri url, Action<bool> done);

    /// <summary>Opens url in the default web browser, whatever its type; false when there is none.</summary>
    bool Open(Uri url);

    /// <summary>Shows the file in Explorer.</summary>
    void Reveal(string path);
}
