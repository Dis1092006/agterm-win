namespace AgTerm.Xterm;

/// <summary>
/// The WebView2 seam the bridge talks through. Two adapters exist because the WinRT projection
/// (WinUI's WebView2 control) and the Win32 .NET SDK (the hidden-window test host) expose
/// CoreWebView2 as DIFFERENT types with identical shapes; the bridge itself stays host-free.
/// All members are thread-affine to the WebView2's UI thread — callers marshal.
/// </summary>
public interface IWebViewScriptHost
{
    /// <summary>Serve a local folder as https://hostName/ (SetVirtualHostNameToFolderMapping).</summary>
    void MapVirtualHostToFolder(string hostName, string folderPath);

    void Navigate(string url);

    /// <summary>Evaluate script in the page; the result arrives JSON-encoded (ExecuteScriptAsync).</summary>
    Task<string> ExecuteScriptAsync(string script);

    /// <summary>PostWebMessageAsJson — arrives in the page as a parsed object.</summary>
    void PostJson(string json);

    /// <summary>WebMessageReceived with the message as JSON (the page's postMessage side).</summary>
    event Action<string>? JsonMessageReceived;
}
