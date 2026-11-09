using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

/// <summary>Upstream eb29d556: cmd+shift+d moved from dashboard to toggle_horizontal_split and
/// dashboard moved to cmd+shift+g. A keymap.conf that already used one of those chords keeps it:
/// the new action's default is vacated unless the file maps that action itself.</summary>
public class KeymapCompatTests
{
    private static readonly Chord CmdShiftD = new(Modifiers.Command | Modifiers.Shift, "d");
    private static readonly Chord CmdShiftG = new(Modifiers.Command | Modifiers.Shift, "g");

    private static (AgTerm.Core.Keymap.Keymap Keymap, List<KeymapDiagnostic> Diagnostics) Parse(string text) =>
        KeymapParser.Parse(text);

    [Fact]
    public void TheShippedDefaultsAreTheUpstreamOnes()
    {
        Assert.Equal(CmdShiftD, AgTerm.Core.Keymap.Keymap.Empty.EquivalentFor(BuiltinAction.ToggleHorizontalSplit));
        Assert.Equal(CmdShiftG, AgTerm.Core.Keymap.Keymap.Empty.EquivalentFor(BuiltinAction.Dashboard));
    }

    [Theory]
    [InlineData("map cmd+shift+d dashboard\n")]
    [InlineData("map cmd+shift+d toggle_split\nmap cmd+d new_session\n")]
    [InlineData("command \"Deploy\" cmd+shift+d ./deploy.sh\n")]
    public void AnOldConfigThatUsedCmdShiftDKeepsItAndHorizontalSplitShipsKeyless(string text)
    {
        var (keymap, diagnostics) = Parse(text);

        Assert.Empty(diagnostics);
        Assert.Null(keymap.EquivalentFor(BuiltinAction.ToggleHorizontalSplit));
        Assert.Contains(BuiltinAction.ToggleHorizontalSplit, keymap.UnboundDefaults);
    }

    [Fact]
    public void ACommandThatUsedCmdShiftDStillOwnsIt()
    {
        var (keymap, _) = Parse("command \"Deploy\" cmd+shift+d ./deploy.sh\n");

        Assert.Equal("cmd+shift+d", Assert.Single(keymap.Commands).Shortcut);
    }

    [Fact]
    public void MappingHorizontalSplitItselfKeepsItsOverrideAndNoDefaultIsVacated()
    {
        var (keymap, diagnostics) = Parse("map cmd+shift+d dashboard\nmap cmd+shift+h toggle_horizontal_split\n");

        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "h"),
            keymap.EquivalentFor(BuiltinAction.ToggleHorizontalSplit));
        Assert.Equal(CmdShiftD, keymap.EquivalentFor(BuiltinAction.Dashboard));
        Assert.Empty(keymap.UnboundDefaults);
    }

    [Fact]
    public void MappingHorizontalSplitToItsOwnDefaultNeedsNoVacating()
    {
        var (keymap, diagnostics) = Parse("map cmd+shift+d toggle_horizontal_split\n");

        Assert.Empty(diagnostics);
        Assert.Equal(CmdShiftD, keymap.EquivalentFor(BuiltinAction.ToggleHorizontalSplit));
        Assert.Empty(keymap.UnboundDefaults);
    }

    [Theory]
    [InlineData("map cmd+shift+g toggle_flag\n")]
    [InlineData("command \"Grep\" cmd+shift+g rg\n")]
    public void AnOldConfigThatUsedCmdShiftGKeepsItAndDashboardShipsKeyless(string text)
    {
        var (keymap, diagnostics) = Parse(text);

        Assert.Empty(diagnostics);
        Assert.Null(keymap.EquivalentFor(BuiltinAction.Dashboard));
        Assert.Contains(BuiltinAction.Dashboard, keymap.UnboundDefaults);
    }

    [Fact]
    public void MappingDashboardItselfKeepsItsOverrideWhenAnotherActionHoldsCmdShiftG()
    {
        var (keymap, diagnostics) = Parse("map cmd+shift+g toggle_flag\nmap cmd+shift+k dashboard\n");

        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "k"), keymap.EquivalentFor(BuiltinAction.Dashboard));
        Assert.Equal(CmdShiftG, keymap.EquivalentFor(BuiltinAction.ToggleFlag));
        Assert.Empty(keymap.UnboundDefaults);
    }

    [Fact]
    public void AVacatedDefaultComesBackWhenTheCommandThatTriggeredItLosesItsKeybind()
    {
        // both commands claim cmd+shift+d, so both are dropped and nothing holds the chord
        var (keymap, diagnostics) = Parse("command \"A\" cmd+shift+d a\ncommand \"B\" cmd+shift+d b\n");

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(CmdShiftD, keymap.EquivalentFor(BuiltinAction.ToggleHorizontalSplit));
        Assert.Empty(keymap.UnboundDefaults);
    }

    [Fact]
    public void AnUntouchedKeymapVacatesNothing()
    {
        var (keymap, _) = Parse("map cmd+e new_session\n");

        Assert.Empty(keymap.UnboundDefaults);
    }

    [Fact]
    public void AVacatedActionsChordIsFreeForACustomCommandWithoutAConflictDiagnostic()
    {
        var (_, diagnostics) = Parse("command \"Deploy\" cmd+shift+d ./deploy.sh\n");

        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("conflicts with a built-in"));
    }

    // upstream 7f450d17: toggle_overlay ships on cmd+shift+j, a chord older files may already use
    private static readonly Chord CmdShiftJ = new(Modifiers.Command | Modifiers.Shift, "j");

    [Fact]
    public void ToggleOverlayShipsOnCmdShiftJAndCanBeRemapped()
    {
        Assert.Equal(CmdShiftJ, AgTerm.Core.Keymap.Keymap.Empty.EquivalentFor(BuiltinAction.ToggleOverlay));

        var (keymap, diagnostics) = Parse("map ctrl+shift+y toggle_overlay\n");

        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Control | Modifiers.Shift, "y"), keymap.EquivalentFor(BuiltinAction.ToggleOverlay));
    }

    [Theory]
    [InlineData("map cmd+shift+j toggle_workspace_filter\n")]
    [InlineData("command \"Old binding\" cmd+shift+j echo ok\n")]
    public void AnExistingCmdShiftJBindingIsNotBrokenByTheToggleOverlayDefault(string text)
    {
        var (keymap, diagnostics) = Parse(text);

        Assert.Empty(diagnostics);
        Assert.Null(keymap.EquivalentFor(BuiltinAction.ToggleOverlay));
        Assert.Contains(BuiltinAction.ToggleOverlay, keymap.UnboundDefaults);
        if (text.StartsWith("map", StringComparison.Ordinal))
            Assert.Equal(CmdShiftJ, keymap.EquivalentFor(BuiltinAction.ToggleWorkspaceFilter));
        else
            Assert.Equal("cmd+shift+j", Assert.Single(keymap.Commands).Shortcut);
    }
}
