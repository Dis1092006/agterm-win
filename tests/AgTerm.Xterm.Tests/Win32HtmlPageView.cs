using AgTerm.Core.Model;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// IHtmlPageView over the Win32 .NET SDK's WebView2, the test-side twin of the app's WinRT adapter
/// (see Win32ScriptHost for why there are two). Real input never reaches the hidden test window, so
/// a test raises UserInput itself.
/// </summary>
public sealed class Win32HtmlPageView : IHtmlPageView
{
    private readonly WebView2 _control;
    private readonly CoreWebView2 _core;
    private string? _scriptId;

    public Win32HtmlPageView(WebView2 control)
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
    }

    public event Action<HtmlPageNavigationStarting>? NavigationStarting;
    public event Action? ContentLoading;
    public event Action<HtmlPageNavigationCompleted>? NavigationCompleted;
    public event Action? PageChanged;
    public event Action? ProcessFailed;
    public event Action? UserInput;

    public CoreWebView2 Core => _core;

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

    // the fixture disposes its test controls itself, possibly before the test closes the page
    public void Stop()
    {
        if (!_control.IsDisposed) _core.Stop();
    }

    public void Close() => _control.Dispose();

    public void RaiseUserInput() => UserInput?.Invoke();

    private bool Starting(string url, HtmlNavigationTarget target, bool user)
    {
        var args = new HtmlPageNavigationStarting(url, target, user);
        NavigationStarting?.Invoke(args);
        return args.Cancel;
    }
}
