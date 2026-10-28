namespace AgTerm.Core.Keymap;

/// <summary>App-owned shortcuts, not BuiltinActions. A user binding reserves its first chord.</summary>
public static class SettingsShortcuts
{
    private static readonly PhysicalChord[] Chords = [new(false, true, false, ","), new(true, false, false, ",")];

    public static bool Opens(Keymap keymap, PhysicalChord chord) =>
        Chords.Contains(chord) &&
        !keymap.BuiltinOverrides.Values.SelectMany(WindowsChordDecoder.Presses).Contains(chord) &&
        !keymap.Commands.Any(command => Keybinds.Parse(command.Shortcut) is { Length: > 0 } parsed &&
            WindowsChordDecoder.Presses(parsed[0]).Contains(chord));

    public static HotkeyManifest AddToManifest(Keymap keymap, HotkeyManifest manifest) =>
        manifest with { BuiltinSingles = manifest.BuiltinSingles.Concat(Chords.Where(chord => Opens(keymap, chord))).Distinct().ToArray() };
}
