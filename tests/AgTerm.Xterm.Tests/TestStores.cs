using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Stores for tests that only exercise in-memory projections. A bare <c>new AppStore()</c> resolves
/// its persistence and its side files against the process-wide default state dir — the developer's
/// real one — so every test store names a throwaway directory instead. Nothing is written unless the
/// test mutates the store, and the directory is never created when it isn't.
/// </summary>
internal static class TestStores
{
    public static AppStore Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("N"));
        return new AppStore(persistence: new PersistenceStore(dir), stateDirectory: dir);
    }
}
