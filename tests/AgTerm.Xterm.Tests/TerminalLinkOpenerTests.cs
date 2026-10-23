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
    public void Missing_paths_return_a_normal_notice()
    {
        var started = new List<ProcessStartInfo>();
        var notice = new TerminalLinkOpener(started.Add, _ => false).Open(@"C:\missing.txt");
        Assert.Empty(started);
        Assert.Equal(new LiveFailureNotice("Path not found", @"Path not found: C:\missing.txt"), notice);
    }

    [Theory]
    [InlineData(@"C:\Program Files\x status pending", @"C:\Program Files\x status pending")]
    [InlineData(@"C:\Program Files\x status pending", @"C:\Program Files\x status")]
    [InlineData(@"C:\Program Files\x status pending", @"C:\Program Files\x")]
    [InlineData(@"C:\\Program Files\\x status pending", @"C:\Program Files\x")]
    [InlineData(@"C:\Program Files\x title: pending", @"C:\Program Files\x")]
    public void Unquoted_paths_reveal_the_longest_existing_candidate(string target, string existing)
    {
        var started = new List<ProcessStartInfo>();
        var probed = new List<string>();
        var notice = new TerminalLinkOpener(started.Add, path =>
        {
            probed.Add(path);
            return path == existing || path == @"C:\Program";
        }).Open(target, allowPathFallback: true);
        Assert.Null(notice);
        Assert.Equal(existing, probed[^1]);
        Assert.DoesNotContain(@"C:\Program", probed);
        Assert.Equal(AgTerm.Core.Model.HudLinkPolicy.ExplorerSelectArguments(existing), Assert.Single(started).Arguments);
    }

    [Fact]
    public void All_missing_candidates_report_the_full_detected_path()
    {
        var probed = new List<string>();
        const string target = @"C:\Program Files\x status pending";
        var notice = new TerminalLinkOpener(_ => Assert.Fail("unexpected launch"), path =>
        {
            probed.Add(path);
            return false;
        }).Open(target, allowPathFallback: true);
        Assert.Equal(new[] { target, @"C:\Program Files\x status", @"C:\Program Files\x", @"C:\Program" }, probed);
        Assert.Equal(new LiveFailureNotice("Path not found", "Path not found: " + target), notice);
    }

    [Theory]
    [InlineData(@"C:\safe\NUL status", @"C:\safe\NUL status")]
    [InlineData(@"C:\safe\x title: pending", @"C:\safe\x")]
    public void Each_fallback_candidate_is_validated_before_probing(string target, string expectedProbe)
    {
        var probed = new List<string>();
        new TerminalLinkOpener(_ => { }, path => { probed.Add(path); return false; })
            .Open(target, allowPathFallback: true);
        Assert.Equal(new[] { expectedProbe }, probed);
    }

    [Theory]
    [InlineData(@"C:\Program Files\x status", false, @"C:\Program Files\x status")]
    [InlineData("file:///C:/Program%20Files/x%20status", true, @"C:\Program Files\x status")]
    public void Explicit_targets_do_not_try_shorter_paths(string target, bool allowPathFallback, string expectedProbe)
    {
        var probed = new List<string>();
        new TerminalLinkOpener(_ => Assert.Fail("unexpected launch"), path =>
        {
            probed.Add(path);
            return path == @"C:\Program Files\x";
        }).Open(target, allowPathFallback);
        Assert.Equal(new[] { expectedProbe }, probed);
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
