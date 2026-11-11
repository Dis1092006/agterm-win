using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>
/// Owns the web view of every page open in one store's slots, keyed by the page's id, so a page
/// survives session switches and the undoable close (upstream HtmlOverlayRegistry). It drops a page
/// only when the store releases it: the owner wires the store's release to Release. All members run on
/// the UI thread.
/// </summary>
public sealed class HtmlOverlayRegistry
{
    private readonly IHtmlPageStore _pageStore;
    private readonly IHtmlBrowser _browser;
    private readonly Func<string?, HtmlOverlayTheme> _theme;
    // a page is here from the moment it is asked for, while its web view is still being made
    private readonly Dictionary<Guid, Task<HtmlOverlayPage>> _realizing = [];
    private readonly Dictionary<Guid, HtmlOverlayPage> _pages = [];

    /// <param name="theme">The terminal theme as a page's default style, given the overlay's own
    /// background color.</param>
    public HtmlOverlayRegistry(IHtmlPageStore pageStore, IHtmlBrowser browser, Func<string?, HtmlOverlayTheme> theme)
    {
        _pageStore = pageStore;
        _browser = browser;
        _theme = theme;
    }

    /// <summary>The live page for overlay, making its web view and loading it on first use. A page
    /// released before its web view arrives comes back closed.</summary>
    public Task<HtmlOverlayPage> RealizeAsync(HtmlOverlay overlay, Func<Task<IHtmlPageView>> createView,
                                              string? backgroundColor = null)
    {
        if (_realizing.TryGetValue(overlay.Id, out var known)) return known;
        var done = new TaskCompletionSource<HtmlOverlayPage>();
        _realizing[overlay.Id] = done.Task;
        _ = CreateAsync(overlay, createView, backgroundColor, done);
        return done.Task;
    }

    private async Task CreateAsync(HtmlOverlay overlay, Func<Task<IHtmlPageView>> createView, string? backgroundColor,
                                   TaskCompletionSource<HtmlOverlayPage> done)
    {
        try
        {
            var view = await createView();
            var page = new HtmlOverlayPage(overlay, _pageStore, view, _browser, _theme(backgroundColor), backgroundColor);
            if (!_realizing.TryGetValue(overlay.Id, out var current) || current != done.Task)
            {
                page.Close();
                done.SetResult(page);
                return;
            }
            _pages[overlay.Id] = page;
            await page.StartAsync();
            done.SetResult(page);
        }
        catch (Exception e)
        {
            if (_realizing.TryGetValue(overlay.Id, out var current) && current == done.Task)
            {
                _realizing.Remove(overlay.Id);
                _pages.Remove(overlay.Id);
            }
            done.SetException(e);
        }
    }

    public HtmlOverlayPage? Existing(Guid id) => _pages.GetValueOrDefault(id);

    public void Release(Guid id)
    {
        _realizing.Remove(id);
        if (_pages.Remove(id, out var page)) page.Close();
    }

    /// <summary>Runs a history or hand-off action for the control API and the toolbar.</summary>
    public string? Navigate(Guid id, HtmlNavigation navigation) =>
        _pages.TryGetValue(id, out var page) ? page.Navigate(navigation) : Core.Protocol.OverlayHtmlError.NotRealized;

    /// <summary>Asks the store for a reload of the session's page, then reloads a page already shown
    /// at once, even with no host to push the new revision.</summary>
    public HtmlOverlayCommandFailure? Reload(AppStore store, Guid sessionId, HtmlReloadTarget target)
    {
        if (store.ReloadHtmlOverlay(sessionId, pane: null, target) is { } failure) return failure;
        if (store.SessionWithId(sessionId)?.HtmlOverlay is { } overlay) Apply(overlay);
        return null;
    }

    /// <summary>Gives a live page the model's latest value; it reloads when the revision moved. The
    /// live app passes a copy, since the store changes its own off the UI thread.</summary>
    public void Apply(HtmlOverlay overlay) => Existing(overlay.Id)?.Apply(overlay);

    /// <summary>Gives every page the current theme.</summary>
    public async Task RefreshThemesAsync()
    {
        foreach (var page in _pages.Values.ToList())
            await page.ApplyThemeAsync(_theme(page.BackgroundColor));
    }
}
