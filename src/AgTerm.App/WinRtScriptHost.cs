using Microsoft.Web.WebView2.Core;

namespace AgTerm.App;

/// <summary>
/// IWebViewScriptHost over the WinRT CoreWebView2 (the WinUI WebView2 control's core) — the
/// app-side twin of the test suite's Win32 adapter (see IWebViewScriptHost for why two exist).
/// </summary>
public sealed class WinRtScriptHost : AgTerm.Xterm.IWebViewScriptHost
{
    private readonly CoreWebView2 _core;

    public WinRtScriptHost(CoreWebView2 core)
    {
        _core = core;
        _core.WebMessageReceived += (_, e) => JsonMessageReceived?.Invoke(e.WebMessageAsJson);
    }

    public event Action<string>? JsonMessageReceived;

    public void MapVirtualHostToFolder(string hostName, string folderPath) =>
        _core.SetVirtualHostNameToFolderMapping(
            hostName, folderPath, CoreWebView2HostResourceAccessKind.Allow);

    public void Navigate(string url) => _core.Navigate(url);

    public async Task<string> ExecuteScriptAsync(string script) =>
        await _core.ExecuteScriptAsync(script);

    public void PostJson(string json) => _core.PostWebMessageAsJson(json);
}
