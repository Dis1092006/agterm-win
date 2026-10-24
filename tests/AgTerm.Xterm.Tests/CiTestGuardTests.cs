namespace AgTerm.Xterm.Tests;

public class CiTestGuardTests
{
    [Fact]
    public void Ci_stops_after_a_failure_and_collects_hang_diagnostics()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("--blame-hang-timeout 4m --blame-hang-dump-type mini -- 'xUnit.StopOnFail=true'",
            workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("name: test-results\n          path: TestResults\n          retention-days: 14",
            workflow.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_runs_one_test_project_at_a_time()
    {
        // the live ConPTY and WebView2 suites starve the runner's thread pool when they run side by side,
        // and their 5 second waits time out
        Assert.Contains("run: dotnet test AgTerm.slnx -m:1 --no-build", ReadWorkflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_publish_runs_on_main_and_manual_runs_but_not_pull_requests()
    {
        var workflow = ReadWorkflow().Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("  pull_request:\n  push:\n    branches: [main]\n  workflow_dispatch:",
            workflow, StringComparison.Ordinal);
        Assert.Contains("  test:\n    runs-on: windows-latest\n", workflow, StringComparison.Ordinal);
        Assert.Contains("  publish-cli:\n    if: github.event_name != 'pull_request'\n",
            workflow, StringComparison.Ordinal);
    }

    private static string ReadWorkflow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;

        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
        return File.ReadAllText(Path.Combine(root, ".github", "workflows", "tests.yml"));
    }
}
