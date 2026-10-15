using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class WindowsExplorerTests
{
    [Fact]
    public void Explorer_starts_from_the_windows_directory_by_its_full_path()
    {
        var start = WindowsExplorer.StartInfo("/select,\"C:\\x\"");

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            start.FileName);
        Assert.True(Path.IsPathFullyQualified(start.FileName), start.FileName);
        Assert.Equal("/select,\"C:\\x\"", start.Arguments);
        Assert.False(start.UseShellExecute);
    }

    // a bare name is searched for in the app directory and the current directory before C:\Windows
    [Theory]
    [InlineData("src", "AgTerm.App", "MainWindow.xaml.cs")]
    [InlineData("src", "AgTerm.Xterm", "SidebarController.cs")]
    public void No_caller_starts_explorer_by_its_bare_name(params string[] path)
    {
        var source = File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

        Assert.Contains("WindowsExplorer.StartInfo(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"explorer.exe\"", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
