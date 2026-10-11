using AgTerm.App;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

// Ported from upstream PickCustomRowTests, PaletteSearchKeysTests, PaletteTests (selectionStep) and
// the ControlPickUITests rules that do not need a window.
public sealed class PickPaletteTests
{
    private static PendingPick Pick(ControlPickItem[] items, string? query = null, bool allowCustom = false, string? selection = null, string? prompt = null) =>
        new("P", items, prompt, query, allowCustom, selection);

    private static ControlPickItem Item(string id, string label, string? subtitle = null) => new() { Id = id, Label = label, Subtitle = subtitle };

    private static PaletteController Open(PendingPick pick)
    {
        var controller = new PaletteController();
        controller.Open(PickPalette.Items(pick), PickPalette.Options(pick));
        return controller;
    }

    [Fact]
    public void Empty_query_keeps_the_callers_order_and_enter_picks_the_first_item()
    {
        var controller = Open(Pick(new[] { Item("z", "Zulu"), Item("a", "Alpha") }));
        Assert.Equal(new[] { "z", "a" }, controller.FilteredItems.Select(i => i.Id));
        Assert.Equal(ControlPickResult.Picked("z", "Zulu", 0), PickPalette.Outcome(controller.AcceptSelected()!));
    }

    [Fact]
    public void A_blank_query_with_a_newline_keeps_the_callers_order()
    {
        var controller = Open(Pick(new[] { Item("z", "Zulu"), Item("a", "Alpha") }));
        controller.SetQuery(" \n ");
        Assert.Equal(new[] { "z", "a" }, controller.FilteredItems.Select(i => i.Id));
    }

    [Fact]
    public void Matching_uses_the_label_only()
    {
        var controller = Open(Pick(new[] { Item("keep", "Keep", "delete everything"), Item("del", "Delete") }));
        controller.SetQuery("everything");
        Assert.Empty(controller.FilteredItems);
    }

    [Fact]
    public void A_filtered_pick_reports_the_callers_index()
    {
        var controller = Open(Pick(new[] { Item("a", "Alpha"), Item("b", "Bravo"), Item("c", "Charlie") }));
        controller.SetQuery("char");
        Assert.Equal(ControlPickResult.Picked("c", "Charlie", 2), PickPalette.Outcome(controller.AcceptSelected()!));
    }

    [Fact]
    public void A_prefilled_query_opens_filtered()
    {
        var controller = Open(Pick(new[] { Item("a", "Alpha"), Item("b", "Bravo") }, query: "bra"));
        Assert.Equal("bra", controller.Query);
        Assert.Equal(new[] { "b" }, controller.FilteredItems.Select(i => i.Id));
    }

    [Fact]
    public void Selection_highlights_its_row_once_and_a_query_edit_resets_to_the_first_row()
    {
        var controller = Open(Pick(new[] { Item("a", "Alpha"), Item("b", "Bravo"), Item("c", "Charlie") }, selection: "c"));
        Assert.Equal("c", controller.SelectedItem!.Id);

        controller.SetQuery("a");

        Assert.Equal(0, controller.SelectedIndex);
    }

    [Fact]
    public void A_selection_the_query_hides_falls_back_to_the_first_row()
    {
        var controller = Open(Pick(new[] { Item("a", "Alpha"), Item("b", "Bravo") }, query: "alp", selection: "b"));
        Assert.Equal("a", controller.SelectedItem!.Id);
    }

    [Fact]
    public void Custom_row_appears_only_when_allowed_and_nothing_matches()
    {
        var allowed = Open(Pick(new[] { Item("a", "Alpha") }, allowCustom: true));
        allowed.SetQuery("  zzz  ");
        var row = Assert.Single(allowed.FilteredItems);
        Assert.Equal("Use \"zzz\"", row.Title);
        Assert.Equal(ControlPickResult.Custom("zzz"), PickPalette.Outcome(allowed.AcceptSelected()!));

        var matching = Open(Pick(new[] { Item("a", "Alpha") }, allowCustom: true));
        matching.SetQuery("alp");
        Assert.False(Assert.Single(matching.FilteredItems).IsCustomAnswer);

        var disallowed = Open(Pick(new[] { Item("a", "Alpha") }));
        disallowed.SetQuery("zzz");
        Assert.Empty(disallowed.FilteredItems);
    }

    [Fact]
    public void An_itemless_picker_lists_nothing_until_typed()
    {
        var controller = Open(Pick(Array.Empty<ControlPickItem>(), allowCustom: true));
        Assert.Empty(controller.FilteredItems);
        Assert.Null(controller.AcceptSelected());
        controller.SetQuery("name");
        Assert.True(Assert.Single(controller.FilteredItems).IsCustomAnswer);
    }

    [Fact]
    public void Blank_ids_and_whitespace_labels_do_not_throw()
    {
        var controller = Open(Pick(new[] { Item("", "   "), Item("x", "X") }));
        Assert.Equal(2, controller.FilteredItems.Count);
    }

    [Fact]
    public void Placeholder_defaults_only_when_the_prompt_is_null()
    {
        Assert.Equal("Select…", PickPalette.Placeholder(Pick(new[] { Item("a", "A") })));
        Assert.Equal("", PickPalette.Placeholder(Pick(new[] { Item("a", "A") }, prompt: "")));
        Assert.Equal("Choose", PickPalette.Placeholder(Pick(new[] { Item("a", "A") }, prompt: "Choose")));
    }

    [Theory]
    [InlineData(0x4A, true, false, false, 1)]   // Ctrl+J
    [InlineData(0x4B, true, false, false, -1)]  // Ctrl+K
    [InlineData(0x4A, false, false, false, 0)]  // plain j types
    [InlineData(0x4A, true, true, false, 0)]    // Ctrl+Shift+J is not a step
    [InlineData(0x4B, true, false, true, 0)]    // Ctrl+Alt+K is not a step
    [InlineData(0x4C, true, false, false, 0)]   // Ctrl+L
    public void Selection_step_is_ctrl_j_and_ctrl_k_only(int key, bool control, bool shift, bool alt, int expected)
    {
        Assert.Equal(expected, PaletteKeys.SelectionStep(key, control, shift, alt));
    }
}
