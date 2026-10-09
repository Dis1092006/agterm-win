using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// WebView2 writes a browser profile (cache, cookies, crash metrics) next to the exe unless it is
/// given a folder. Next to the exe is the publish directory, so a clean publish has to delete a
/// profile that the last run left behind, and some of its files stay memory-mapped after the app
/// exits. The profile belongs with the app's other per-user state instead.
/// </summary>
public class WebViewProfileDirectoryTests
{
    [Fact]
    public void ProfileSitsUnderTheStateDirectory()
    {
        var stateDir = Path.Combine(Path.GetTempPath(), "agterm-wv2-profile-test");

        Assert.Equal(Path.Combine(stateDir, "webview2"), WebViewProfile.DirectoryIn(stateDir));
    }

    [Fact]
    public void ProfileFollowsTheStateDirectoryOverride()
    {
        // AGTERM_STATE_DIR isolates every other stored file, so two instances pointed at different
        // state dirs must not share one browser profile either.
        var first = WebViewProfile.DirectoryIn(Path.Combine(Path.GetTempPath(), "agterm-a"));
        var second = WebViewProfile.DirectoryIn(Path.Combine(Path.GetTempPath(), "agterm-b"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ProfileIsNotNextToTheExecutable()
    {
        // the whole point: WebView2's own default is <exe>.WebView2 in the publish directory.
        var resolved = WebViewProfile.DirectoryIn(Path.Combine(Path.GetTempPath(), "agterm-state"));

        Assert.DoesNotContain(AppContext.BaseDirectory, resolved);
    }

    [Fact]
    public void ProfileDefaultsToTheRealUserStateDirectory()
    {
        // no explicit state dir: the app's own ~\.agterm-win, the same root as workspaces.json
        Assert.Equal(
            Path.Combine(PersistenceStore.RealUserDirectory, "webview2"),
            WebViewProfile.Directory());
    }
}
