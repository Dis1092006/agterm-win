using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;

namespace AgTerm.App;

/// <summary>
/// IHtmlPageView over the WinUI WebView2 control, the app-side twin of the test suite's Win32 adapter.
/// Each page runs in its own InPrivate profile, so pages share no cookies or storage with each other
/// or with the terminal page (upstream gives a file page a nonpersistent data store).
/// </summary>
public sealed class WinRtHtmlPageView : IHtmlPageView
{
    private readonly WebView2 _control;
    private readonly CoreWebView2 _core;
    private string? _scriptId;

    private WinRtHtmlPageView(WebView2 control)
    {
        _control = control;
        _core = control.CoreWebView2;
        _core.NavigationStarting += (_, e) => e.Cancel = Starting(e.Uri, HtmlNavigationTarget.MainFrame, e.IsUserInitiated);
        _core.FrameNavigationStarting += (_, e) => e.Cancel = Starting(e.Uri, HtmlNavigationTarget.Subframe, e.IsUserInitiated);
        _core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Starting(e.Uri, HtmlNavigationTarget.NewWindow, e.IsUserInitiated);
        };
        _core.ContentLoading += (_, _) => ContentLoading?.Invoke();
        _core.NavigationCompleted += (_, e) => NavigationCompleted?.Invoke(new HtmlPageNavigationCompleted(
            e.IsSuccess, e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled, e.WebErrorStatus.ToString()));
        _core.SourceChanged += (_, _) => PageChanged?.Invoke();
        _core.DocumentTitleChanged += (_, _) => PageChanged?.Invoke();
        _core.HistoryChanged += (_, _) => PageChanged?.Invoke();
        _core.ProcessFailed += (_, e) =>
        {
            if (WebViewProcessFailure.Notice(e.ProcessFailedKind.ToString()) is not null) ProcessFailed?.Invoke();
        };
        _core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        _core.DownloadStarting += (_, e) => e.Cancel = true;
        // the control marks its pointer events handled once it passes them to the page; script cannot
        // raise these, and the page's keys go to the browser process where the app cannot see them
        control.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => UserInput?.Invoke()), true);
    }

    /// <summary>Makes the control's web view in a profile of its own; the control must be in the tree.</summary>
    public static async Task<WinRtHtmlPageView> CreateAsync(WebView2 control, CoreWebView2Environment environment, Guid id)
    {
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.IsInPrivateModeEnabled = true;
        options.ProfileName = $"agterm-page-{id:N}";
        await control.EnsureCoreWebView2Async(environment, options);
        return new WinRtHtmlPageView(control);
    }

    public event Action<HtmlPageNavigationStarting>? NavigationStarting;
    public event Action? ContentLoading;
    public event Action<HtmlPageNavigationCompleted>? NavigationCompleted;
    public event Action? PageChanged;
    public event Action? ProcessFailed;
    public event Action? UserInput;

    public bool CanGoBack => _core.CanGoBack;

    public bool CanGoForward => _core.CanGoForward;

    public string? Source => _core.Source;

    public string? DocumentTitle => _core.DocumentTitle;

    public void Configure(bool javascript)
    {
        var settings = _core.Settings;
        settings.IsScriptEnabled = javascript;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsStatusBarEnabled = false;
    }

    public void MapVirtualHost(string host, string folder) =>
        _core.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Allow);

    public async Task SetDocumentScriptAsync(string script)
    {
        if (_scriptId is not null) _core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
        _scriptId = await _core.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    public void Navigate(string url) => _core.Navigate(url);

    public void NavigateToString(string html) => _core.NavigateToString(html);

    public void Reload() => _core.Reload();

    public void GoBack() => _core.GoBack();

    public void GoForward() => _core.GoForward();

    public void Stop() => _core.Stop();

    public void Close()
    {
        (_control.Parent as Panel)?.Children.Remove(_control);
        _control.Close();
    }

    private bool Starting(string url, HtmlNavigationTarget target, bool user)
    {
        var args = new HtmlPageNavigationStarting(url, target, user);
        NavigationStarting?.Invoke(args);
        return args.Cancel;
    }
}
