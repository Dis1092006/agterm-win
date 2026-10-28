using AgTerm.Core.Keymap;
using KeymapModel = AgTerm.Core.Keymap.Keymap;

namespace AgTerm.Core.Tests.Keymap;

public sealed class SettingsShortcutsTests
{
    [Fact]
    public void Both_openers_are_app_only_and_explicit_bindings_and_leaders_keep_priority()
    {
        var alt = new PhysicalChord(false, true, false, ",");
        var ctrl = new PhysicalChord(true, false, false, ",");
        Assert.True(SettingsShortcuts.Opens(KeymapModel.Empty, alt));
        Assert.True(SettingsShortcuts.Opens(KeymapModel.Empty, ctrl));
        Assert.False(SettingsShortcuts.Opens(KeymapModel.Empty, ctrl with { Shift = true }));
        var (keymap, diagnostics) = KeymapParser.Parse("map opt+, new_session\ncommand \"Test\" ctrl+,>x echo test");
        Assert.Empty(diagnostics);
        Assert.False(SettingsShortcuts.Opens(keymap, alt));
        Assert.False(SettingsShortcuts.Opens(keymap, ctrl));
        var manifest = new HotkeyRouter(KeymapModel.Empty).CreateManifest(2, _ => true);
        var extended = SettingsShortcuts.AddToManifest(KeymapModel.Empty, manifest);
        Assert.Contains(alt, extended.BuiltinSingles);
        Assert.Contains(ctrl, extended.BuiltinSingles);
        Assert.Equal(2, extended.Generation);
    }
}
