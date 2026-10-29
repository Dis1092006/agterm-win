using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer.Tests;

public sealed class DetachedQuickTests
{
    [Fact]
    public void Bare_zoom_targets_the_window_and_quick_zoom_is_independent()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-detached-contract-").FullName;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions { StateDir = directory, ConfigDir = directory });
            var actions = host.Actions;
            actions.SetQuickTerminal("show");
            var zoom = actions.SetSurfaceZoom(null, null, ControlToggleMode.On);
            Assert.StartsWith("surface:", zoom.Result?.Id);
            var target = actions.Zoom.Target;
            Assert.True(actions.SetSurfaceZoom("quick", "not-a-window", ControlToggleMode.On).Ok);
            Assert.Equal(target, actions.Zoom.Target);
            Assert.True(actions.Quick.IsZoomed);
            Assert.True(actions.SetQuickTerminal("show").Ok);
            Assert.True(actions.SetQuickTerminal("hide").Ok);
            Assert.Equal(target, actions.Zoom.Target);
            Assert.Equal("surface not available: quick",
                actions.SetSurfaceZoom("quick", null, ControlToggleMode.Off).Error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Quick_environment_has_no_window_identity()
    {
        var environment = AgTerm.Core.Model.SurfaceEnvironment.QuickTerminal(Guid.NewGuid(), "socket");
        Assert.Equal(2, environment.Count);
        Assert.DoesNotContain("AGTERM_WINDOW_ID", environment.Keys);
    }

    [Fact]
    public void Control_show_repins_a_human_summon_and_blur_does_not_hide_it()
    {
        var quick = new HeadlessQuickTerminal();
        quick.Show();
        quick.SetKeyOwnership(true);
        Assert.True(quick.HoldsKey);
        quick.Show(dismissOnFocusLoss: false);
        quick.SetKeyOwnership(false);
        Assert.True(quick.Visible);
        Assert.False(quick.HoldsKey);
        quick.Hide();
        quick.Show();
        quick.SetKeyOwnership(true);
        quick.SetKeyOwnership(false);
        Assert.False(quick.Visible);
    }

    [Fact]
    public void Zoom_belongs_to_the_panel_and_hidden_zoom_hide_is_refused()
    {
        var quick = new HeadlessQuickTerminal();
        Assert.False(quick.SetZoom(ControlToggleMode.Off));
        quick.Show();
        Assert.True(quick.SetZoom(ControlToggleMode.On));
        Assert.True(quick.IsZoomed);
        quick.Hide();
        Assert.False(quick.IsZoomed);
        Assert.False(quick.SetZoom(ControlToggleMode.Off));
    }

    [Fact]
    public void Can_show_gates_every_summon_and_shell_exit_resets_the_pin()
    {
        var allowed = false;
        var quick = new HeadlessQuickTerminal { CanShow = () => allowed };
        quick.Show(dismissOnFocusLoss: false);
        Assert.False(quick.Visible);
        allowed = true;
        quick.Show(dismissOnFocusLoss: false);
        quick.ShellExited();
        quick.Show();
        quick.SetKeyOwnership(true);
        quick.SetKeyOwnership(false);
        Assert.False(quick.Visible);
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    public void Toolbar_reshow_suppression_ends_at_300_milliseconds(int elapsed, bool visible)
    {
        var clock = new Clock();
        var quick = new HeadlessQuickTerminal(clock);
        quick.Show();
        quick.SetKeyOwnership(true);
        quick.SetKeyOwnership(false);
        clock.Timestamp = elapsed;
        quick.Toggle();
        Assert.Equal(visible, quick.Visible);
    }

    [Fact]
    public void Global_hotkey_and_control_show_bypass_toolbar_blur_suppression()
    {
        var quick = new HeadlessQuickTerminal(new Clock());
        quick.Show();
        quick.SetKeyOwnership(false);
        quick.Toggle(fromGlobalHotkey: true);
        Assert.True(quick.Visible);
        quick.SetKeyOwnership(false);
        quick.Show(dismissOnFocusLoss: false);
        quick.SetKeyOwnership(false);
        Assert.True(quick.Visible);
    }

    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
    }
}
