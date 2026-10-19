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
    private readonly int _uiThreadId;

    public Win32ScriptHost(CoreWebView2 core)
    {
        _core = core;
        _uiThreadId = Environment.CurrentManagedThreadId;
        _core.WebMessageReceived += (_, e) => JsonMessageReceived?.Invoke(e.WebMessageAsJson);
    }

    public event Action<string>? JsonMessageReceived;

    public void MapVirtualHostToFolder(string hostName, string folderPath)
    {
        VerifyUiThread();
        _core.SetVirtualHostNameToFolderMapping(
            hostName, folderPath, CoreWebView2HostResourceAccessKind.Allow);
    }

    public void Navigate(string url)
    {
        VerifyUiThread();
        _core.Navigate(url);
    }

    public Task<string> ExecuteScriptAsync(string script)
    {
        VerifyUiThread();
        return _core.ExecuteScriptAsync(script);
    }

    public void PostJson(string json)
    {
        VerifyUiThread();
        _core.PostWebMessageAsJson(json);
    }

    // CoreWebView2 throws on a wrong-thread call only when COM cannot marshal it. Some machines
    // have a proxy registered for its interfaces (for example by Adobe Acrobat), and there the call
    // silently succeeds. This check makes the tests fail the same way on every machine.
    private void VerifyUiThread()
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
            throw new InvalidOperationException(
                "CoreWebView2 members can only be accessed from the UI thread.");
    }
}
