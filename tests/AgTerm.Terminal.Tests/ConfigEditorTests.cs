namespace AgTerm.Terminal.Tests;

/// <summary>
/// The editor the palette's Edit Keymap row opens in an overlay (upstream
/// ConfigPaths.editorCommand: $VISUAL, then $EDITOR, then a fallback). On Windows the fallback is
/// Microsoft Edit (edit.exe) when it is on PATH, then Notepad. The command names the file only;
/// the overlay runs in the file's directory, so the file name needs no path.
/// </summary>
public class ConfigEditorTests
{
    private static string? NoWhich(string _) => null;

    [Fact]
    public void Visual_wins_over_everything()
    {
        var command = ConfigEditor.Command(
            "keymap.conf",
            which: _ => @"C:\Windows\system32\edit.exe",
            env: name => name switch { "VISUAL" => "code --wait", "EDITOR" => "vim", _ => null });

        Assert.Equal("code --wait keymap.conf", command);
    }

    [Fact]
    public void Editor_is_used_when_visual_is_unset_or_blank()
    {
        var command = ConfigEditor.Command(
            "keymap.conf",
            which: _ => @"C:\Windows\system32\edit.exe",
            env: name => name switch { "VISUAL" => "  ", "EDITOR" => "vim", _ => null });

        Assert.Equal("vim keymap.conf", command);
    }

    [Fact]
    public void Microsoft_edit_is_the_fallback_when_it_is_on_path()
    {
        var probed = new List<string>();
        var command = ConfigEditor.Command(
            "keymap.conf",
            which: name =>
            {
                probed.Add(name);
                return name == "edit.exe" ? @"C:\Windows\system32\edit.exe" : null;
            },
            env: name => name == "EDITOR" ? "" : null);

        Assert.Equal("edit.exe keymap.conf", command);
        Assert.Equal(["edit.exe"], probed);
    }

    [Fact]
    public void Notepad_is_the_last_fallback()
    {
        var command = ConfigEditor.Command("keymap.conf", NoWhich, _ => null);

        Assert.Equal("notepad.exe keymap.conf", command);
    }

    [Fact]
    public void Production_overload_uses_the_process_environment_and_path()
    {
        var command = ConfigEditor.Command("keymap.conf");

        Assert.EndsWith(" keymap.conf", command, StringComparison.Ordinal);
    }
}
