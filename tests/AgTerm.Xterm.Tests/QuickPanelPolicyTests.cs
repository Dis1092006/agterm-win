using AgTerm.App;
using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using System.Runtime.InteropServices;

namespace AgTerm.Xterm.Tests;

public sealed class QuickPanelPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Activation_uses_observed_foreground_and_only_notifies_a_failed_human_summon(
        bool pinned, bool ownsForeground)
    {
        var quick = new HeadlessQuickTerminal();
        quick.Show(dismissOnFocusLoss: !pinned);
        quick.SetKeyOwnership(true);
        var traces = new List<string>();
        var reports = new List<string>();

        QuickPanelFocus.ActivationCompleted(quick, ownsForeground, traces.Add, reports.Add);

        Assert.Equal(ownsForeground, quick.HoldsKey);
        Assert.Equal(pinned || ownsForeground, quick.Visible);
        Assert.Equal(!ownsForeground && pinned ? 1 : 0, traces.Count);
        Assert.Equal(!ownsForeground && !pinned ? 1 : 0, reports.Count);
    }

    [Fact]
    public void Pinning_a_visible_human_panel_makes_a_denied_activation_trace_only()
    {
        var quick = new HeadlessQuickTerminal();
        quick.Show();
        quick.Show(dismissOnFocusLoss: false);
        var traces = new List<string>();
        var reports = new List<string>();

        QuickPanelFocus.ActivationCompleted(quick, false, traces.Add, reports.Add);

        Assert.True(quick.Visible);
        Assert.False(quick.HoldsKey);
        Assert.Single(traces);
        Assert.Empty(reports);
    }

    [Fact]
    public void Repeated_background_control_shows_never_add_failure_notices()
    {
        var quick = new HeadlessQuickTerminal();
        var traces = new List<string>();
        var reports = new List<string>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            quick.Show(dismissOnFocusLoss: false);
            QuickPanelFocus.ActivationCompleted(quick, false, traces.Add, reports.Add);
        }

        Assert.True(quick.Visible);
        Assert.False(quick.HoldsKey);
        Assert.Equal(3, traces.Count);
        Assert.Empty(reports);
    }

    [Fact]
    public void A_denied_focus_return_is_trace_only()
    {
        var traces = new List<string>();

        QuickPanelFocus.RestoreDenied(traces.Add);

        Assert.Equal("Windows denied returning focus to the previous foreground window.", Assert.Single(traces));
    }

    [Fact]
    public void Registration_replaces_once_reports_conflicts_and_removal_unregisters()
    {
        var registrations = 0;
        var removals = 0;
        var failure = 0;
        string? error = null;
        var binding = new GlobalHotkeyBinding((key, modifiers) =>
        {
            registrations++;
            Assert.Equal(0x20, key);
            Assert.Equal(3u, modifiers & 3);
            return failure;
        }, () => removals++, message => error = message);
        var chord = new Chord(Modifiers.Control | Modifiers.Option, "space");
        binding.Apply(chord);
        binding.Apply(chord);
        Assert.Equal(1, registrations);
        binding.Apply(null);
        Assert.Equal(1, removals);
        failure = 1409;
        binding.Apply(chord);
        Assert.Contains("1409", error);
        Assert.Null(binding.RegisteredChord);
        binding.Apply(null);
        Assert.Null(error);
    }

    [Collection("webview2")]
    public sealed class GlobalHotkeyNativeTests(WebView2Fixture fixture)
    {
        [Fact]
        public async Task Native_receiver_survives_without_a_panel_and_reports_conflict_then_releases_the_chord()
        {
            await fixture.RunAsync(_ =>
            {
                using var firstWindow = new System.Windows.Forms.Form();
                using var secondWindow = new System.Windows.Forms.Form();
                var fired = 0;
                string? firstError = null;
                string? secondError = null;
                using var first = new GlobalHotkey(firstWindow.Handle, () => fired++, e => firstError = e, _ => { });
                using var second = new GlobalHotkey(secondWindow.Handle, () => { }, e => secondError = e, _ => { });
                var chord = new Chord(Modifiers.Control | Modifiers.Option | Modifiers.Shift, "9");
                first.Apply(chord);
                Assert.Null(firstError);
                second.Apply(chord);
                Assert.Contains("1409", secondError);
                SendMessage(firstWindow.Handle, 0x0312, new IntPtr(0x514B), IntPtr.Zero);
                Assert.Equal(1, fired);
                first.Apply(null);
                second.Apply(chord);
                Assert.Null(secondError);
                return Task.CompletedTask;
            });
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }

    [Fact]
    public void Geometry_uses_work_area_negative_origins_and_target_dpi()
    {
        var area = new QuickPanelFrame(-2560, 0, 2560, 1400);
        Assert.Equal(new QuickPanelFrame(-2105, 175, 1650, 1050), QuickPanelGeometry.Frame(area, 1.5, false));
        Assert.Equal(area, QuickPanelGeometry.Frame(area, 1.5, true));
        Assert.Equal(new QuickPanelFrame(50, 25, 900, 450),
            QuickPanelGeometry.Frame(new(0, 0, 1000, 500), 2, false));
    }
}
