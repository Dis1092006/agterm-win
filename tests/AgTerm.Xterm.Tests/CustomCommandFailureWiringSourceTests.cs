using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

public sealed class CustomCommandFailureWiringSourceTests
{
    private static string Window() => File.ReadAllText(Path.Combine(
        FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

    private static string Method(string source, string signature) =>
        Regex.Match(source, Regex.Escape(signature) + @"[\s\S]*?
    \}?
").Value;

    [Fact]
    public void The_start_helper_keeps_the_info_bar_and_hands_panels_on_without_opening_one()
    {
        var helper = Method(Window(), "private string? StartCustomCommand(");
        Assert.NotEmpty(helper);
        Assert.Contains("QueueFailureNotice(CustomCommandFailureNotice.NotStarted(invocation.Name, ex.Message), fatal: false);", helper, StringComparison.Ordinal);
        Assert.Contains("CustomCommandFailureReporter.ForNotStarted(invocation, ex.Message)", helper, StringComparison.Ordinal);
        Assert.Contains("OpenFailurePanelAfterExitAsync(invocation, handle)", helper, StringComparison.Ordinal);
        // keymap run calls the helper inside the host gate, where the store coordinator would deadlock
        Assert.DoesNotContain("OpenCommandFailureHud", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("MutateAsync", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_zero_exit_opens_its_panel_after_the_process_finished()
    {
        var method = Method(Window(), "private async Task OpenFailurePanelAfterExitAsync(");
        Assert.NotEmpty(method);
        Assert.Matches(new Regex(@"await handle\.Finished"), method);
        Assert.Contains("CustomCommandFailureReporter.ForExit(invocation, exit)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_opens_from_a_pool_thread_through_the_store_coordinator_and_logs_what_it_did_not_do()
    {
        var opener = Method(Window(), "private Task OpenFailurePanelAsync(");
        Assert.NotEmpty(opener);
        Assert.Matches(new Regex(@"Task\.Run\(\(\) =>\s*host\.StoreCoordinator\.MutateAsync\("), opener);
        Assert.Contains("host.Host.Actions.OpenCommandFailureHud(", opener, StringComparison.Ordinal);
        Assert.Contains("result.Fallback", opener, StringComparison.Ordinal);
        Assert.Contains("result.Response.Error", opener, StringComparison.Ordinal);
        Assert.Contains("System.Diagnostics.Debug.WriteLine", opener, StringComparison.Ordinal);
        Assert.Contains("invocation.Name", opener, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("AgTerm.slnx not found above the test output.");
    }
}
