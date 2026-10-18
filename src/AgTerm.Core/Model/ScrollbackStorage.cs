using System.Text;

namespace AgTerm.Core.Model;

/// <summary>
/// Host-free on-disk location of saved pane output (a Windows-only addition, see
/// docs/PLATFORM-NOTES.md): a scrollback\ subdir of the state directory with one VT text file per
/// session pane. The host writes a file at shutdown and takes (reads and deletes) it when the pane
/// is realized again; the store removes a session's files when the session is permanently
/// destroyed. Every operation is best effort: a failure loses the saved output, never the session.
///
/// Each function takes an optional stateDir override (default null = the AGTERM_STATE_DIR /
/// LocalAppData resolution) so tests can inject a temp directory.
/// </summary>
public static class ScrollbackStorage
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>&lt;stateDir&gt;\scrollback — NOT created.</summary>
    public static string DirectoryPath(string? stateDir = null) =>
        Path.Combine(stateDir ?? PersistenceStore.DefaultDirectory, "scrollback");

    /// <summary>&lt;stateDir&gt;\scrollback\&lt;SESSION-UUID&gt;.&lt;pane&gt;.vt</summary>
    public static string FilePath(Guid sessionId, StatusPane pane, string? stateDir = null) =>
        Path.Combine(
            DirectoryPath(stateDir),
            $"{sessionId.ToString("D").ToUpperInvariant()}.{pane.ToString().ToLowerInvariant()}.vt");

    /// <summary>Save one pane's serialized output, replacing any earlier file. Empty text removes
    /// the file instead, so a cleared pane does not come back with stale output.</summary>
    public static void Write(Guid sessionId, StatusPane pane, string text, string? stateDir = null)
    {
        var path = FilePath(sessionId, pane, stateDir);
        if (string.IsNullOrEmpty(text))
        {
            TryDelete(path);
            return;
        }

        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(DirectoryPath(stateDir));
            File.WriteAllText(temp, text, Utf8NoBom);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
        }
    }

    /// <summary>Read and delete one pane's saved output; null when there is none. Deleting on read
    /// means a crash before the next clean shutdown does not replay the same output twice.</summary>
    public static string? Take(Guid sessionId, StatusPane pane, string? stateDir = null)
    {
        var path = FilePath(sessionId, pane, stateDir);
        string text;
        try
        {
            text = File.ReadAllText(path, Utf8NoBom);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        TryDelete(path);
        return text.Length == 0 ? null : text;
    }

    /// <summary>Remove every saved pane of a session (best effort; a no-op when none exists).
    /// Called when the session is permanently destroyed.</summary>
    public static void Remove(Guid sessionId, string? stateDir = null)
    {
        foreach (var pane in Enum.GetValues<StatusPane>())
        {
            TryDelete(FilePath(sessionId, pane, stateDir));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
