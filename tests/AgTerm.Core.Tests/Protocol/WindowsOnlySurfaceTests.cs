using System.Text.Json;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

// parity/windows-only.json lists what the port adds that upstream does not have. If a later
// upstream version adds the same name, the reverse checks fail at that pin update and the owner
// decides.
public class WindowsOnlySurfaceTests
{
    [Fact]
    public void The_list_names_the_workspace_theme_and_the_windows_only_settings()
    {
        Assert.Equal(["workspace.theme"], ParitySurface.WindowsOnly("commands"));
        Assert.Equal(["theme"], ParitySurface.WindowsOnly("workspaceNode"));
        Assert.Equal(["theme"], ParitySurface.WindowsOnly("workspaceSnapshot"));
        Assert.Equal(["sidebarThemeColors", "restoreScrollback"], ParitySurface.WindowsOnly("settings"));
    }

    [Theory]
    [InlineData("commands")]
    [InlineData("workspaceNode")]
    public void No_windows_only_control_entry_exists_upstream(string section)
    {
        Assert.All(ParitySurface.WindowsOnly(section),
            name => Assert.DoesNotContain(name, ParitySurface.Upstream(section)));
    }

    [Fact]
    public void No_windows_only_snapshot_field_exists_upstream()
    {
        Assert.All(ParitySurface.WindowsOnly("workspaceSnapshot"),
            name => Assert.DoesNotContain(name, ParitySurface.UpstreamPersistence("workspaceSnapshot")));
    }

    [Fact]
    public void No_windows_only_setting_exists_upstream()
    {
        Assert.NotEmpty(ParitySurface.UpstreamSettings());
        Assert.All(ParitySurface.WindowsOnly("settings"),
            name => Assert.DoesNotContain(name, ParitySurface.UpstreamSettings()));
    }

    [Fact]
    public void Every_windows_only_entry_says_why()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/windows-only.json")));
        var entries = doc.RootElement.EnumerateObject()
            .Where(section => section.Value.ValueKind == JsonValueKind.Array)
            .SelectMany(section => section.Value.EnumerateArray())
            .ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString())));
    }
}
