namespace AgTerm.Core.Model;

/// <summary>
/// Host-free on-disk location of rendered .text watermark PNGs — a watermarks\ subdir of the state
/// directory (honoring AGTERM_STATE_DIR for test isolation, like the snapshot/settings files). Both
/// the host renderer (which writes the PNGs) and the host-free store (which removes a session's PNG
/// when the session is permanently destroyed) share one path definition.
///
/// Each function takes an optional stateDir override (default null = the AGTERM_STATE_DIR /
/// LocalAppData resolution) so tests can inject a temp directory without mutating process-global
/// env (parallel-safe).
/// </summary>
public static class WatermarkStorage
{
    /// <summary>&lt;stateDir&gt;\watermarks — NOT created. Use EnsureDirectory before writing.</summary>
    public static string DirectoryPath(string? stateDir = null) =>
        Path.Combine(stateDir ?? PersistenceStore.DefaultDirectory, "watermarks");

    /// <summary>DirectoryPath, created lazily (best effort). Called before rendering a .text PNG.</summary>
    public static string EnsureDirectory(string? stateDir = null)
    {
        var dir = DirectoryPath(stateDir);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return dir;
    }

    /// <summary>The per-session rendered-text PNG path (&lt;stateDir&gt;\watermarks\&lt;SESSION-UUID&gt;.png).</summary>
    public static string RenderedTextPath(Guid sessionId, string? stateDir = null) =>
        Path.Combine(DirectoryPath(stateDir), sessionId.ToString("D").ToUpperInvariant() + ".png");

    /// <summary>Remove a session's rendered .text PNG (best effort), so the state dir doesn't
    /// accumulate stale files. A no-op when none exists. Called on watermark clear AND when the
    /// owning session is permanently removed — a .text watermark always re-renders its PNG on apply,
    /// so an over-eager removal is self-healing.</summary>
    public static void RemoveRenderedText(Guid sessionId, string? stateDir = null)
    {
        try
        {
            File.Delete(RenderedTextPath(sessionId, stateDir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
