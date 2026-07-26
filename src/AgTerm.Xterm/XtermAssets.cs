namespace AgTerm.Xterm;

/// <summary>Locates the terminal page + pinned xterm.js bundles, which ship in an Assets folder
/// next to the AgTerm.Xterm assembly (vendored, never a CDN — versions in Assets/vendor/MANIFEST.md).</summary>
public static class XtermAssets
{
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Assets");
}
