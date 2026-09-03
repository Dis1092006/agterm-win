using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class FailurePresentationTests
{
    [Fact]
    public void MainWindow_routes_runtime_failures_to_one_native_error_banner()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root,
            "src", "AgTerm.App", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"FailureBanner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Severity=\"Error\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FailureReported = QueueFailureNotice", source, StringComparison.Ordinal);
        Assert.Contains("Terminal view failed to initialize", source, StringComparison.Ordinal);
        Assert.Contains("ReportSettingsReloadFailure", source, StringComparison.Ordinal);
        Assert.Contains("ApplyLiveThemeWithFailureAsync", source, StringComparison.Ordinal);
        Assert.Contains("ShowFailure(LiveFailureNotice", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_surface_failure_reports_UI_notice_without_faulting_host_or_pipe()
    {
        var stateDirectory = Directory.CreateTempSubdirectory("agterm-page-failure-").FullName;
        var notices = new List<LiveFailureNotice>();
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDirectory,
                PipeName = "agterm-page-failure-" + Guid.NewGuid().ToString("N"),
                HomeDirectory = stateDirectory,
                FailureReported = notices.Add,
                OpenSurface = (_, _) => Task.FromException<IHeadlessSurface>(
                    new InvalidOperationException("WebView2 unavailable")),
            });

            var notice = Assert.Single(notices);
            Assert.Equal("Terminal failed to start", notice.Title);
            Assert.Equal("WebView2 unavailable", notice.Message);
            host.Start();
            var response = new ControlPipeClient(host.PipeName).Send(
                new ControlRequest { Cmd = Command.Tree });
            Assert.True(response.Ok, response.Error);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}