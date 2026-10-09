namespace AgTerm.Core.Model;

/// <summary>
/// Where WebView2 keeps its browser profile: cache, cookies, local storage and crash metrics.
///
/// Left to itself WebView2 creates &lt;exe&gt;.WebView2 beside the executable, which for a published
/// build is the publish directory. Two things follow from that. A clean publish has to delete a
/// profile the last run wrote, and the runtime keeps some of those files memory-mapped past exit
/// (EBWebView\CrashpadMetrics-active.pma), so the delete fails while the app is or recently was
/// running. The profile is per-user data, so it goes with the rest of it.
/// </summary>
public static class WebViewProfile
{
    /// <summary>The profile directory under the app's state dir (%AGTERM_STATE_DIR% or
    /// ~\.agterm-win), beside workspaces.json and the windows\ directory.</summary>
    public static string Directory() => DirectoryIn(PersistenceStore.RealUserDirectory);

    /// <summary>The profile directory under a named state dir. Taking the state dir as a parameter
    /// keeps the %AGTERM_STATE_DIR% override working: two instances pointed at different state dirs
    /// get different profiles, as they do for every other stored file.</summary>
    public static string DirectoryIn(string stateDirectory) =>
        Path.Combine(stateDirectory, DirectoryName);

    /// <summary>The subdirectory name inside the state dir.</summary>
    public const string DirectoryName = "webview2";
}
