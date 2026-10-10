namespace AgTerm.Core.Model;

/// <summary>
/// Renaming a freshly written temp file over a stored file, which is how every file here is saved:
/// a crashed write then leaves the old file rather than half of the new one.
/// </summary>
public static class AtomicFile
{
    /// <summary>How long a rename keeps trying before the lock counts as someone else's problem.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Renames <paramref name="temp"/> onto <paramref name="destination"/>, replacing it.
    ///
    /// On Windows the rename fails with UnauthorizedAccessException while another process holds the
    /// destination open, and a virus scanner or the search indexer opens a file for a moment right
    /// after it is written. That is the whole lifetime of the problem, so the rename is retried and
    /// the save succeeds instead of throwing out of the caller. A lock that outlasts the timeout is
    /// rethrown: a caller that could not store its state has to hear about it.
    /// </summary>
    public static void Replace(string temp, string destination, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        var delayMs = 10;
        while (true)
        {
            try
            {
                File.Move(temp, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                if (DateTime.UtcNow >= deadline) throw;
                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, 100);
            }
        }
    }
}
