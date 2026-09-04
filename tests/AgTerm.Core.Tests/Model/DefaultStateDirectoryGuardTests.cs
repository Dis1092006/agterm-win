using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// The implicit default state directory is the real user state dir. A test that constructs a store
/// without naming a directory would write there, overwriting the developer's own workspaces. These
/// tests pin the guard that turns that silent write into a failure.
/// </summary>
public class DefaultStateDirectoryGuardTests
{
    [Fact]
    public void DefaultDirectoryUnderATestRunWithNoStateDirThrows()
    {
        var previous = Environment.GetEnvironmentVariable("AGTERM_STATE_DIR");
        Environment.SetEnvironmentVariable("AGTERM_STATE_DIR", null);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => PersistenceStore.DefaultDirectory);
            Assert.Contains("AGTERM_STATE_DIR", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGTERM_STATE_DIR", previous);
        }
    }

    [Fact]
    public void DefaultDirectoryUnderATestRunHonorsAnExplicitStateDir()
    {
        var previous = Environment.GetEnvironmentVariable("AGTERM_STATE_DIR");
        var expected = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AGTERM_STATE_DIR", expected);
        try
        {
            Assert.Equal(expected, PersistenceStore.DefaultDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGTERM_STATE_DIR", previous);
        }
    }

    [Fact]
    public void ConstructingAStoreWithNoDirectoryUnderATestRunThrows()
    {
        var previous = Environment.GetEnvironmentVariable("AGTERM_STATE_DIR");
        Environment.SetEnvironmentVariable("AGTERM_STATE_DIR", null);
        try
        {
            Assert.Throws<InvalidOperationException>(() => new PersistenceStore());
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGTERM_STATE_DIR", previous);
        }
    }
}
