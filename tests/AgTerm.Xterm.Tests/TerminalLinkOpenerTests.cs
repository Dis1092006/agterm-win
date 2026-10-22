using System.Diagnostics;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public class TerminalLinkOpenerTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://host/file.txt")]
    public void Urls_use_the_system_handler_without_probing_the_disk(string target)
    {
        var started = new List<ProcessStartInfo>();
        var opener = new TerminalLinkOpener(started.Add, _ => throw new InvalidOperationException("unexpected probe"));
        opener.Open(target);
        var info = Assert.Single(started);
        Assert.Equal(target, info.FileName);
        Assert.True(info.UseShellExecute);
    }

    [Theory]
    [InlineData(@"C:\my files\report.txt", @"C:\my files\report.txt")]
    [InlineData("file:///C:/my%20files/report.exe", @"C:\my files\report.exe")]
    [InlineData("file:///C:/my%20files/report.cmd", @"C:\my files\report.cmd")]
    [InlineData("file:///C:/my%20files/report.lnk", @"C:\my files\report.lnk")]
    [InlineData(@"C:\", @"C:\")]
    public void Files_and_directories_are_only_selected_in_trusted_Explorer(string target, string path)
    {
        var started = new List<ProcessStartInfo>();
        var probed = new List<string>();
        var opener = new TerminalLinkOpener(started.Add, value => { probed.Add(value); return true; });
        opener.Open(target);
        Assert.Equal(path, Assert.Single(probed));
        var info = Assert.Single(started);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), info.FileName);
        Assert.False(info.UseShellExecute);
        Assert.Equal(AgTerm.Core.Model.HudLinkPolicy.ExplorerSelectArguments(path), info.Arguments);
    }

    [Fact]
    public void Missing_paths_are_ignored()
    {
        var started = new List<ProcessStartInfo>();
        new TerminalLinkOpener(started.Add, _ => false).Open(@"C:\missing.txt");
        Assert.Empty(started);
    }

    [Theory]
    [InlineData(@"\\server\share\x")]
    [InlineData("file://remote/C:/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData(@"C:\NUL")]
    public void Rejected_targets_never_probe_or_launch(string target) =>
        new TerminalLinkOpener(_ => Assert.Fail("unexpected launch"), _ => throw new InvalidOperationException("unexpected probe"))
            .Open(target);

    [Fact]
    public void Launch_failures_are_not_hidden() =>
        Assert.Throws<InvalidOperationException>(() =>
            new TerminalLinkOpener(_ => throw new InvalidOperationException("launch failed"), _ => true)
                .Open(@"C:\x.txt"));
}
