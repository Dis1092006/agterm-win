using AgTerm.Core.Keymap;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public class ControlKeymapTests
{
    private const string Path = "keymap.conf";

    private static ControlKeymap Project(string text)
    {
        var (keymap, diagnostics) = KeymapParser.Parse(text);
        return ControlKeymapProjection.Project(keymap, diagnostics, Path);
    }

    private static ControlKeymapAction Action(ControlKeymap list, string name) =>
        list.Actions.Single(a => a.Action == name);

    [Fact]
    public void An_empty_keymap_lists_every_builtin_action_in_order()
    {
        var list = Project("");

        Assert.Equal(41, list.Actions.Length);
        Assert.Equal(BuiltinActions.All.Select(a => a.WireName()), list.Actions.Select(a => a.Action));
        Assert.Equal(Path, list.Path);
    }

    [Fact]
    public void An_unmoved_action_shows_its_windows_default_in_windows_spelling()
    {
        var list = Project("");

        Assert.Equal("ctrl+alt+n", Action(list, "new_window").Chord);
        Assert.Equal("alt+d", Action(list, "toggle_split").Chord);
        Assert.Null(Action(list, "toggle_split").Overridden);
    }

    [Fact]
    public void An_action_without_a_chord_has_no_chord_field()
    {
        Assert.Null(Action(Project(""), "rename_session").Chord);
    }

    [Fact]
    public void A_map_line_to_another_key_marks_the_action_overridden()
    {
        var action = Action(Project("map ctrl+shift+d toggle_split"), "toggle_split");

        Assert.Equal("ctrl+shift+d", action.Chord);
        Assert.True(action.Overridden);
    }

    [Fact]
    public void A_map_line_to_the_same_windows_key_is_not_an_override()
    {
        var action = Action(Project("map ctrl+alt+n new_window"), "new_window");

        Assert.Equal("ctrl+alt+n", action.Chord);
        Assert.Null(action.Overridden);
    }

    [Fact]
    public void Commands_keep_file_order_and_their_written_shortcut()
    {
        var list = Project("command \"Build\" cmd+shift+b echo b\ncommand \"Palette only\" echo p\n");

        Assert.Equal(["Build", "Palette only"], list.Commands.Select(c => c.Name));
        Assert.Equal("cmd+shift+b", list.Commands[0].Shortcut);
        Assert.Null(list.Commands[1].Shortcut);
        Assert.All(list.Commands, c => Assert.False(c.Repeats));
    }

    [Fact]
    public void Commands_read_back_their_error_options()
    {
        var list = Project("command \"quiet\" echo q\ncommand \"loud\" --error-hud --error-position bottom --error-pane right echo l\n");

        var quiet = list.Commands[0];
        Assert.False(quiet.ErrorHud);
        Assert.Equal("center", quiet.ErrorPosition);
        Assert.Null(quiet.ErrorPane);

        var loud = list.Commands[1];
        Assert.True(loud.ErrorHud);
        Assert.Equal("bottom-center", loud.ErrorPosition);
        Assert.Equal("right", loud.ErrorPane);
    }

    [Fact]
    public void Command_fields_serialize_in_upstream_order()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Keymap = Project("command \"quiet\" echo q\ncommand \"loud\" --error-hud --error-pane left echo l"),
            },
        };

        var json = ControlJson.Serialize(response);

        Assert.Contains("{\"name\":\"quiet\",\"errorHud\":false,\"errorPosition\":\"center\",\"repeats\":false}", json);
        Assert.Contains("{\"name\":\"loud\",\"errorHud\":true,\"errorPosition\":\"center\",\"errorPane\":\"left\",\"repeats\":false}", json);
    }

    [Fact]
    public void Diagnostics_list_the_parser_ones_then_the_windows_shadowing_ones()
    {
        var empty = Project("");
        Assert.Equal(2, empty.Diagnostics.Length);
        Assert.All(empty.Diagnostics, d => Assert.Equal(0, d.Line));
        Assert.Contains("'ctrl+alt+up'", empty.Diagnostics[0].Message);

        var broken = Project("bogus line");
        Assert.Equal(3, broken.Diagnostics.Length);
        Assert.Equal(1, broken.Diagnostics[0].Line);
    }

    [Fact]
    public void The_payload_serializes_with_upstream_field_names_and_omissions()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult { Keymap = Project("map ctrl+shift+x new_window\ncommand \"Build\" echo b") },
        };

        var json = ControlJson.Serialize(response);

        Assert.Contains("\"keymap\":{\"path\":", json);
        Assert.Contains("\"overridden\":true", json);
        Assert.Contains("\"repeats\":false", json);
        Assert.DoesNotContain("\"menu\"", json);
        Assert.DoesNotContain("\"alternates\"", json);
        Assert.DoesNotContain("\"overridden\":false", json);
        Assert.DoesNotContain("\"chord\":null", json);
    }

    [Theory]
    [InlineData(true, true, false, "n", "ctrl+alt+n")]
    [InlineData(false, true, false, "d", "alt+d")]
    [InlineData(true, false, true, "x", "ctrl+shift+x")]
    [InlineData(true, false, false, "`", "ctrl+`")]
    public void Spelling_writes_ctrl_alt_shift_then_the_key(bool ctrl, bool alt, bool shift, string key, string expected)
    {
        Assert.Equal(expected, WindowsChordShadowing.Spelling(new PhysicalChord(ctrl, alt, shift, key)));
    }
}
