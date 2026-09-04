using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore AppStoreTestFixtures.swift.</summary>
internal static class AppStoreFixtures
{
    /// <summary>A store backed by a throwaway temp directory so mutation-time saves never touch the
    /// real state path. PersistenceStore creates the directory lazily on first write.</summary>
    public static AppStore MakeStore()
    {
        var dir = TempDir();
        return new(persistence: new PersistenceStore(dir), stateDirectory: dir);
    }

    /// <summary>The same throwaway store, plus the Open Recent store it records closes into and the
    /// persistence it writes through, for the paths that read back what a close or a restore persisted.</summary>
    public static (AppStore Store, RecentClosedStore RecentClosed, PersistenceStore Persistence)
        MakeStoreWithRecentClosed()
    {
        var dir = TempDir();
        var recentClosed = new RecentClosedStore(dir);
        var persistence = new PersistenceStore(dir);
        return (new AppStore(persistence: persistence, recentClosedStore: recentClosed,
                stateDirectory: dir),
            recentClosed, persistence);
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("D"));
}

internal sealed class SpySurface : ITerminalSurface, ICompletedExitCodeSurface
{
    public int TeardownCount;
    public int PromotedCount;

    public int? CompletedExitCode { get; set; }

    public string? PaneToken { get; set; }

    public SpySurface(string paneToken = "")
    {
        PaneToken = paneToken;
    }

    public void Teardown() => TeardownCount++;

    public void PromoteToPrimaryPane() => PromotedCount++;
}
