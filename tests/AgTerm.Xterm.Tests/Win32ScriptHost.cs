using Microsoft.Web.WebView2.Core;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// IWebViewScriptHost over the Win32 .NET SDK's CoreWebView2 — the test-side twin of the app's
/// WinRT adapter (the two CoreWebView2 types are shape-identical but identity-distinct, which is
/// why the seam exists; see IWebViewScriptHost).
/// </summary>
public sealed class Win32ScriptHost : IWebViewScriptHost
{
    private readonly CoreWebView2 _core;

    public Win32ScriptHost(CoreWebView2 core)
    {
        _core = core;
        _core.WebMessageReceived += (_, e) => JsonMessageReceived?.Invoke(e.WebMessageAsJson);
    }

    public event Action<string>? JsonMessageReceived;

    public void MapVirtualHostToFolder(string hostName, string folderPath) =>
        _core.SetVirtualHostNameToFolderMapping(
            hostName, folderPath, CoreWebView2HostResourceAccessKind.Allow);

    public void Navigate(string url) => _core.Navigate(url);

    public Task<string> ExecuteScriptAsync(string script) => _core.ExecuteScriptAsync(script);

    public void PostJson(string json) => _core.PostWebMessageAsJson(json);
}
