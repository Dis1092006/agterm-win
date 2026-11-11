using System.Runtime.InteropServices;
using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

/// <summary>
/// One page in an overlay slot and its web view (upstream HtmlOverlayPage, c63e5158, file sources
/// only). It loads the page, keeps navigation inside the grant, and reports load state, address,
/// title and history into the store by the page's id. It keeps its own copy of what it reported,
/// for the strip on the UI thread, which never reads the store.
/// </summary>
public sealed class HtmlOverlayPage
{
    private readonly IHtmlPageStore _store;
    private readonly IHtmlBrowser _browser;
    private HtmlOverlay _overlay;
    private HtmlOverlayTheme _theme;
    private int _appliedRevision;
    private Guid? _prompt;
    private Action? _dismissPrompt;
    // a declined prompt silences the page until real input reaches its view: script can click links
    // in a loop, but it cannot make real mouse or key events
    private bool _promptsSilenced;
    private bool _onScreen = true;
    // a main-frame load in flight, explicit or started by the page; it must end loaded or failed, so
    // a policy cancel of a page's own redirect reports failed rather than leaving the page loading
    private bool _loadPending;
    // a document this web content process still shows
    private bool _committed;
    // the app's own text load is the one data: navigation a page without a grant may make
    private bool _textLoadExpected;
    // the file the last allowed main-frame navigation went to, to name a missing file when it fails
    private string? _navigatedFile;

    public HtmlOverlayPage(HtmlOverlay overlay, IHtmlPageStore store, IHtmlPageView view, IHtmlBrowser browser,
                           HtmlOverlayTheme theme, string? backgroundColor)
    {
        _overlay = overlay;
        _store = store;
        _browser = browser;
        _theme = theme;
        _appliedRevision = overlay.ReloadRevision;
        Id = overlay.Id;
        View = view;
        BackgroundColor = backgroundColor;
        view.NavigationStarting += OnNavigationStarting;
        view.ContentLoading += OnContentLoading;
        view.NavigationCompleted += OnNavigationCompleted;
        view.PageChanged += ReportPage;
        view.ProcessFailed += OnProcessFailed;
        view.UserInput += OnUserInput;
    }

    public Guid Id { get; }

    public IHtmlPageView View { get; }

    /// <summary>The overlay's own background, which replaces the theme's.</summary>
    public string? BackgroundColor { get; }

    public bool IsClosed { get; private set; }

    public HtmlLoadState LoadState { get; private set; } = HtmlLoadState.Loading;

    public string? LoadError { get; private set; }

    /// <summary>The page shown, its title and its history; null until the first commit.</summary>
    public HtmlPageInfo? Current { get; private set; }

    /// <summary>Raised when LoadState, LoadError or Current change.</summary>
    public event Action? Changed;

    /// <summary>The virtual host a page with a grant is served from. Each page has its own, so pages
    /// share no origin; .example never resolves on a network.</summary>
    public static string VirtualHost(Guid id) => $"agterm-page-{id:N}.example";

    private HtmlSource.File Source => (HtmlSource.File)_overlay.Source;

    // a page without a grant is loaded from its text: it sits at about:blank and cannot navigate, so
    // the file it came from is both the current page and what the user is looking at
    private bool TextLoaded => Source.GrantRoot is null;

    /// <summary>Configures the view and loads the page.</summary>
    public async Task StartAsync()
    {
        View.Configure(_overlay.Javascript);
        if (Source.GrantRoot is { } grant) View.MapVirtualHost(VirtualHost(Id), grant);
        await View.SetDocumentScriptAsync(_theme.Script(themed: true));
        if (IsClosed) return;
        LoadOriginal();
    }

    /// <summary>Takes the model's latest value and reloads when its revision moved.</summary>
    public void Apply(HtmlOverlay overlay)
    {
        _overlay = overlay;
        if (IsClosed || overlay.ReloadRevision == _appliedRevision) return;
        _appliedRevision = overlay.ReloadRevision;
        // the store reset its own copy when it took the reload
        Mirror(HtmlLoadState.Loading, null, overlay.ReloadTarget == HtmlReloadTarget.Current ? Current : null);
        if (overlay.ReloadTarget == HtmlReloadTarget.Current) ReloadShown();
        else LoadOriginal();
    }

    public string? Navigate(HtmlNavigation navigation)
    {
        switch (navigation)
        {
            case HtmlNavigation.Back:
                if (!View.CanGoBack) return OverlayHtmlError.NoHistory(HtmlNavigation.Back);
                View.GoBack();
                break;
            case HtmlNavigation.Forward:
                if (!View.CanGoForward) return OverlayHtmlError.NoHistory(HtmlNavigation.Forward);
                View.GoForward();
                break;
            case HtmlNavigation.Browser:
                // a file page opens its own file, never one it navigated to
                if (!_browser.Open(new Uri(Source.Path))) return OverlayHtmlError.NoBrowser;
                break;
            case HtmlNavigation.Finder:
                _browser.Reveal(PagePath());
                break;
        }
        return null;
    }

    /// <summary>Takes whether the page is shown; a page out of sight asks nothing, and its pending
    /// prompt goes with it.</summary>
    public void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        if (!onScreen) EndPrompt();
    }

    /// <summary>Gives later loads a changed theme and reloads what the page shows. Nothing runs in the
    /// live document: script the app runs there counts as the user's input.</summary>
    public async Task ApplyThemeAsync(HtmlOverlayTheme theme)
    {
        if (IsClosed || theme.Equals(_theme)) return;
        _theme = theme;
        await View.SetDocumentScriptAsync(theme.Script(themed: true));
        if (IsClosed) return;
        ReloadShown();
    }

    public void Close()
    {
        if (IsClosed) return;
        IsClosed = true;
        EndPrompt();
        View.NavigationStarting -= OnNavigationStarting;
        View.ContentLoading -= OnContentLoading;
        View.NavigationCompleted -= OnNavigationCompleted;
        View.PageChanged -= ReportPage;
        View.ProcessFailed -= OnProcessFailed;
        View.UserInput -= OnUserInput;
        View.Stop();
        View.Close();
    }

    private void ReloadShown()
    {
        // before a first commit the web view has nothing to reload, so the source is loaded again
        if (!TextLoaded && _committed)
        {
            _loadPending = true;
            View.Reload();
        }
        else
        {
            LoadOriginal();
        }
    }

    private void LoadOriginal()
    {
        _loadPending = true;
        var path = Source.Path;
        if (ReadFailure(path) is { } failure)
        {
            Fail(failure);
            return;
        }
        if (Source.GrantRoot is { } grant)
        {
            _textLoadExpected = false;
            View.Navigate(PageUrl(grant, path));
            return;
        }
        try
        {
            var html = File.ReadAllText(path, Encoding.UTF8);
            _textLoadExpected = true;
            View.NavigateToString(html);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            _textLoadExpected = false;
            Fail(e.Message);
        }
    }

    private static string? ReadFailure(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return e.Message;
        }
    }

    private string PageUrl(string grant, string path)
    {
        var relative = Path.GetRelativePath(grant, path)
                           .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
                           .Select(Uri.EscapeDataString);
        return $"https://{VirtualHost(Id)}/{string.Join('/', relative)}";
    }

    /// <summary>The file a URL on this page's host names, or null for any other URL.</summary>
    private string? FileOnHost(Uri url)
    {
        if (Source.GrantRoot is not { } grant || !url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps
            || !string.Equals(url.Host, VirtualHost(Id), StringComparison.OrdinalIgnoreCase))
            return null;
        var relative = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/'))
                          .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(grant, relative));
    }

    private void OnNavigationStarting(HtmlPageNavigationStarting args)
    {
        var decision = Decide(args, out var shown, out var file);
        if (decision == HtmlNavigationDecision.OpenExternal) AskToOpen(new Uri(args.Url));
        if (decision == HtmlNavigationDecision.Cancel && args.Target == HtmlNavigationTarget.MainFrame
            && !args.UserInitiated && _loadPending)
            Fail("navigation blocked: " + shown);
        args.Cancel = decision != HtmlNavigationDecision.Allow;
        if (args.Cancel || args.Target != HtmlNavigationTarget.MainFrame) return;
        _loadPending = true;
        _navigatedFile = file;
        SetLoadState(HtmlLoadState.Loading, null);
    }

    // the policy judges a URL on the page's host as the file it serves; a raw file: URL would leave
    // that host and is refused, and the app's own text load is the one data: page allowed
    private HtmlNavigationDecision Decide(HtmlPageNavigationStarting args, out string shown, out string? file)
    {
        shown = args.Url;
        file = null;
        if (args.Target == HtmlNavigationTarget.MainFrame && _textLoadExpected
            && args.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            _textLoadExpected = false;
            file = Source.Path;
            return HtmlNavigationDecision.Allow;
        }
        // a URL System.Uri cannot hold (a long data: page) is nothing the policy would allow
        if (!Uri.TryCreate(args.Url, UriKind.Absolute, out var url)) return HtmlNavigationDecision.Cancel;
        shown = url.AbsoluteUri;
        if (args.Target != HtmlNavigationTarget.NewWindow && url.Scheme == Uri.UriSchemeFile)
            return HtmlNavigationDecision.Cancel;
        if (args.Target != HtmlNavigationTarget.NewWindow && FileOnHost(url) is { } path)
        {
            file = path;
            url = new Uri(path);
            shown = url.AbsoluteUri;
        }
        return HtmlNavigationPolicy.Decide(new HtmlNavigationAction(url, args.Target, args.UserInitiated), _overlay);
    }

    private void OnContentLoading()
    {
        _committed = true;
        ReportPage();
    }

    private void OnNavigationCompleted(HtmlPageNavigationCompleted completed)
    {
        if (completed.IsSuccess)
        {
            _loadPending = false;
            SetLoadState(HtmlLoadState.Loaded, null);
            ReportPage();
            return;
        }
        // a navigation the policy cancelled, or one superseded by the next, is not a failed page: the
        // policy reports its own cancel of a pending load, and the next load has its own outcome
        if (completed.Canceled) return;
        Fail(_navigatedFile is { } file && ReadFailure(file) is { } missing
            ? missing
            : "page load failed: " + (completed.Error ?? "unknown"));
    }

    private void OnProcessFailed()
    {
        _committed = false;
        Fail("web content process terminated");
    }

    private void OnUserInput() => _promptsSilenced = false;

    private void Fail(string message)
    {
        _loadPending = false;
        SetLoadState(HtmlLoadState.Failed, message);
    }

    private string PagePath()
    {
        if (TextLoaded) return Source.Path;
        if (View.Source is { } source && Uri.TryCreate(source, UriKind.Absolute, out var url) && FileOnHost(url) is { } file)
            return file;
        return Source.Path;
    }

    private void ReportPage()
    {
        if (IsClosed || View.Source is not { } source) return;
        var info = new HtmlPageInfo(PagePath(), Title(View.DocumentTitle, source), View.CanGoBack, View.CanGoForward);
        _store.SetPage(Id, info);
        Mirror(LoadState, LoadError, info);
    }

    private void SetLoadState(HtmlLoadState state, string? error)
    {
        _store.SetLoadState(Id, state, error);
        Mirror(state, error, Current);
    }

    private void Mirror(HtmlLoadState state, string? error, HtmlPageInfo? current)
    {
        if (state == LoadState && error == LoadError && Equals(current, Current)) return;
        LoadState = state;
        LoadError = error;
        Current = current;
        Changed?.Invoke();
    }

    // WebView2 names a page without a title by its address less the scheme; upstream reports none
    private static string? Title(string? title, string source)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var scheme = source.IndexOf("://", StringComparison.Ordinal);
        return scheme >= 0 && title == source[(scheme + 3)..] ? null : title;
    }

    // a page's own hand-off asks first, one prompt at a time; a request while one is up is dropped
    private void AskToOpen(Uri url)
    {
        if (!_onScreen || _prompt is not null || _promptsSilenced) return;
        var token = Guid.NewGuid();
        _prompt = token;
        var dismiss = _browser.Confirm(url, approved => Answer(token, url, approved));
        if (_prompt != token) return;
        if (dismiss is null)
        {
            _prompt = null;
            return;
        }
        _dismissPrompt = dismiss;
    }

    private void Answer(Guid token, Uri url, bool approved)
    {
        if (_prompt != token) return;
        _prompt = null;
        _dismissPrompt = null;
        if (approved) _browser.Open(url);
        else _promptsSilenced = true;
    }

    private void EndPrompt()
    {
        var dismiss = _dismissPrompt;
        _prompt = null;
        _dismissPrompt = null;
        dismiss?.Invoke();
    }
}
