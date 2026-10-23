using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

public sealed class KeybindingsCatalogTests
{
    [Fact]
    public void Includes_every_builtin_and_fixed_app_shortcut_in_named_groups()
    {
        var groups = KeybindingsCatalog.Create(AgTerm.Core.Keymap.Keymap.Empty);
        var rows = groups.SelectMany(group => group.Items).ToArray();
        Assert.Equal(42, rows.Length);
        foreach (var action in BuiltinActions.All)
            Assert.Single(rows, row => row.Id == action.WireName());
        var session = Assert.Single(rows, row => row.Id == "new_session");
        Assert.Equal("New Session", session.Title);
        Assert.Equal("Alt+N", session.Shortcut);
        Assert.Equal("Unbound", Assert.Single(rows, row => row.Id == "rename_window").Status);
        Assert.Contains(groups, group => group.Name == "Sessions");
        Assert.Equal("Ctrl+Shift+F1", Assert.Single(rows, row => row.Id == "keyboard-shortcuts").Shortcut);
    }

    [Fact]
    public void Overrides_and_physical_conflicts_match_the_router()
    {
        var (keymap, _) = KeymapParser.Parse("map ctrl+shift+x new_session");
        var rows = KeybindingsCatalog.Create(keymap).SelectMany(group => group.Items).ToArray();
        Assert.Equal("Ctrl+Shift+X", Assert.Single(rows, row => row.Id == "new_session").Shortcut);
        Assert.Equal("Override", Assert.Single(rows, row => row.Id == "new_session").Status);
        var shadowed = Assert.Single(rows, row => row.Id == "previous_attention_session");
        Assert.Contains("Conflict", shadowed.Status);
        Assert.Contains("previous_session", shadowed.Status);
        Assert.Equal("Ctrl+Alt+Up", shadowed.Shortcut);
    }

    [Fact]
    public void Custom_leaders_and_unbound_commands_use_the_effective_manifest()
    {
        var (keymap, diagnostics) = KeymapParser.Parse(
            "command \"Leader\" ctrl+k>j echo ok\ncommand \"Palette only\" echo ok\ncommand \"Collision\" cmd+n echo ok");
        var rows = KeybindingsCatalog.Create(keymap, diagnostics).SelectMany(group => group.Items).ToArray();
        Assert.Equal("Ctrl+K > J", Assert.Single(rows, row => row.Title == "Leader").Shortcut);
        Assert.Equal("Unbound", Assert.Single(rows, row => row.Title == "Palette only").Status);
        // cmd+n folds to Ctrl+N, unlike the Windows Alt+N default, so this is usable.
        Assert.Equal("Ctrl+N", Assert.Single(rows, row => row.Title == "Collision").Shortcut);
    }

    [Fact]
    public void Custom_physical_collisions_are_not_advertised_as_usable()
    {
        var (keymap, diagnostics) = KeymapParser.Parse(
            "command \"First\" cmd+k echo first\ncommand \"Second\" ctrl+k>j echo second\ncommand \"Builtin collision\" ctrl+shift+n echo third");
        var rows = KeybindingsCatalog.Create(keymap, diagnostics).SelectMany(group => group.Items).ToArray();
        Assert.Equal("Custom", Assert.Single(rows, row => row.Title == "First").Status);
        Assert.Contains("First", Assert.Single(rows, row => row.Title == "Second").Status);
        Assert.Contains("new_workspace", Assert.Single(rows, row => row.Title == "Builtin collision").Status);
    }

    [Fact]
    public void Parser_cleared_shortcuts_remain_visible_with_diagnostics()
    {
        var (keymap, diagnostics) = KeymapParser.Parse("command \"Blocked\" alt+n echo no\nbad input");
        var groups = KeybindingsCatalog.Create(keymap, diagnostics);
        Assert.Equal("Unbound", Assert.Single(groups.SelectMany(group => group.Items), row => row.Title == "Blocked").Status);
        var errors = Assert.Single(groups, group => group.Name == "Diagnostics");
        Assert.Contains(errors.Items, row => row.Title.Contains("unknown verb") && row.Id == "line 2");
        Assert.Contains(errors.Items, row => row.Title.Contains("a built-in"));
    }

    [Theory]
    [InlineData("NEW_SESSION", "new_session")]
    [InlineData("sessions alt+n", "new_session")]
    [InlineData("unbound rename", "rename_window")]
    [InlineData("ctrl+shift+f1", "keyboard-shortcuts")]
    public void Search_matches_all_terms_across_title_identifier_group_chord_and_status(string query, string id)
    {
        var filtered = KeybindingsCatalog.Filter(KeybindingsCatalog.Create(AgTerm.Core.Keymap.Keymap.Empty), query);
        Assert.Contains(filtered.SelectMany(group => group.Items), row => row.Id == id);
        Assert.All(filtered, group => Assert.NotEmpty(group.Items));
    }

    [Fact]
    public void Controller_preserves_query_on_reload_and_clears_it_on_reopen()
    {
        var controller = new KeybindingsController();
        controller.Open(KeybindingsCatalog.Create(AgTerm.Core.Keymap.Keymap.Empty));
        Assert.True(controller.IsOpen);
        controller.SetQuery("Ctrl+Shift+X");
        Assert.Empty(controller.Groups);
        var (keymap, _) = KeymapParser.Parse("map ctrl+shift+x new_session");
        controller.Refresh(KeybindingsCatalog.Create(keymap));
        Assert.Equal("Ctrl+Shift+X", controller.Query);
        Assert.Equal("new_session", Assert.Single(Assert.Single(controller.Groups).Items).Id);
        controller.Close();
        Assert.False(controller.IsOpen);
        controller.Open(KeybindingsCatalog.Create(AgTerm.Core.Keymap.Keymap.Empty));
        Assert.Equal("", controller.Query);
        Assert.NotEmpty(controller.Groups);
    }
}
