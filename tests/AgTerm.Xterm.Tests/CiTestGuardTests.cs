namespace AgTerm.Xterm.Tests;

public class CiTestGuardTests
{
    [Fact]
    public void Ci_stops_after_a_failure_and_collects_hang_diagnostics()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;

        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "tests.yml"));

        Assert.Contains("--blame-hang-timeout 4m --blame-hang-dump-type mini -- 'xUnit.StopOnFail=true'",
            workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("name: test-results\n          path: TestResults\n          retention-days: 14",
            workflow.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
