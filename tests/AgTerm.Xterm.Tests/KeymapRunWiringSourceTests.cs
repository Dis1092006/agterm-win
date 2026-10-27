using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

public sealed class KeymapRunWiringSourceTests
{
    private static string Window() => File.ReadAllText(Path.Combine(
        FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

    [Fact]
    public void The_runner_resolves_for_the_session_with_the_cached_selection_and_reports_the_reason()
    {
        var source = Window();
        Assert.Matches(new Regex(
            @"CustomCommandRunner = \(store, sessionId, command\) => RunCustomCommandFromPipe\(host, store, sessionId, command\);"),
            source);
        var runner = Regex.Match(source, @"private string\? RunCustomCommandFromPipe\([\s\S]*?\n    \}\r?\n").Value;
        Assert.NotEmpty(runner);
        Assert.Matches(new Regex(
            @"new CustomCommandExecutionRequest\(\s*command\.Id,\s*CustomCommandSource\.Palette,\s*Selection: PaneSelection\(session\),\s*SessionId: sessionId\)"),
            runner);
        Assert.Contains("[command]", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("_armedKeymap", source, StringComparison.Ordinal);
        Assert.Contains("session is no longer available", runner, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"return StartCustomCommand\(invocation\);"), runner);
        // keymap.run runs inside the host gate: only the surface's own selection read may cross threads
        Assert.DoesNotContain("await", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreCoordinator", runner, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", runner, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherQueue", runner, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            @"private static string PaneSelection\(Session session\) =>\s*\(session\.ActiveSurface as IHeadlessSurface\)\?\.SelectedText \?\? string\.Empty;"),
            source);
    }

    [Fact]
    public void The_pipe_and_the_palette_start_a_command_through_one_helper_that_queues_the_not_started_notice()
    {
        var source = Window();
        var helper = Regex.Match(source, @"private string\? StartCustomCommand\([\s\S]*?\n    \}\r?\n").Value;
        Assert.NotEmpty(helper);
        Assert.Contains("_customCommandRunner.Start(invocation)", helper, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (ex is CustomCommandNotStartedException or Win32Exception)", helper, StringComparison.Ordinal);
        Assert.Contains("QueueFailureNotice(CustomCommandFailureNotice.NotStarted(invocation.Name, ex.Message), fatal: false);", helper, StringComparison.Ordinal);
        Assert.Contains("ObserveTaskFault(handle.Exited", helper, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"await Task\.Run\(\(\) => StartCustomCommand\(invocation\), cancellationToken\);"), source);
        Assert.DoesNotContain("_customCommandRunner.StartAsync", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
