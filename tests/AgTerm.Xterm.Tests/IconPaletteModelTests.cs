using AgTerm.Core.Icons;

namespace AgTerm.Xterm.Tests;

public sealed class IconPaletteModelTests
{
    private static readonly IReadOnlyList<SegoeIcon> Icons =
    [
        new("Home", 0xE80F),
        new("MapPin", 0xE707),
        new("MapLayers", 0xF0E2),
        new("Mail", 0xE715),
        new("Zoom", 0xE71E),
    ];

    private static IconPaletteModel Model(string? current = null, Func<int, bool>? hasGlyph = null) =>
        new(Icons, hasGlyph ?? (_ => true), current);

    private static string?[] Names(IconPaletteModel model) => model.Cells.Select(c => c.Name).ToArray();

    [Fact]
    public void Cells_are_Default_then_every_accepted_icon_in_catalog_order()
    {
        var model = Model(hasGlyph: cp => cp != 0xE715);

        Assert.Equal([null, "Home", "MapPin", "MapLayers", "Zoom"], Names(model));
        Assert.Equal(new IconPaletteCell(null, "Default", null), model.Cells[0]);
        Assert.Equal(new IconPaletteCell("MapPin", "MapPin", "\uE707"), model.Cells[2]);
    }

    [Fact]
    public void The_initial_selection_is_the_current_icon()
    {
        var model = Model("MapPin");

        Assert.Equal(2, model.Selected);
        Assert.Equal("MapPin", model.SelectedName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NoSuchIcon")]
    [InlineData("mappin")]
    public void The_initial_selection_is_Default_for_no_or_unknown_current(string? current)
    {
        var model = Model(current);

        Assert.Equal(0, model.Selected);
        Assert.Null(model.SelectedName);
        Assert.True(model.CanAccept);
    }

    [Fact]
    public void Filter_drops_Default_and_keeps_names_containing_the_text()
    {
        var model = Model();

        model.Filter("map");

        Assert.Equal(["MapPin", "MapLayers"], Names(model));
    }

    [Fact]
    public void Filter_ignores_case()
    {
        var model = Model();
        model.Filter("map");
        var lower = Names(model);

        model.Filter("MAP");

        Assert.Equal(lower, Names(model));
    }

    [Fact]
    public void Filter_trims_the_text()
    {
        var model = Model();

        model.Filter("  map ");

        Assert.Equal(["MapPin", "MapLayers"], Names(model));
    }

    [Fact]
    public void An_empty_filter_brings_Default_back()
    {
        var model = Model();
        model.Filter("map");

        model.Filter("");

        Assert.Equal([null, "Home", "MapPin", "MapLayers", "Mail", "Zoom"], Names(model));
    }

    [Fact]
    public void The_selection_stays_on_the_same_icon_when_it_survives_the_filter()
    {
        var model = Model("MapLayers");

        model.Filter("map");

        Assert.Equal("MapLayers", model.SelectedName);
        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void The_selection_moves_to_the_first_cell_when_it_does_not_survive()
    {
        var model = Model("Zoom");

        model.Filter("map");

        Assert.Equal(0, model.Selected);
        Assert.Equal("MapPin", model.SelectedName);
    }

    [Fact]
    public void A_filter_matching_nothing_leaves_nothing_selected()
    {
        var model = Model("MapPin");

        model.Filter("zzzz");

        Assert.Empty(model.Cells);
        Assert.Null(model.Selected);
        Assert.Null(model.SelectedName);
        Assert.False(model.CanAccept);
    }

    [Fact]
    public void Filtering_back_to_empty_after_no_match_selects_Default()
    {
        var model = Model();
        model.Filter("zzzz");

        model.Filter("");

        Assert.Equal(0, model.Selected);
        Assert.True(model.CanAccept);
    }

    [Fact]
    public void Filtering_back_after_no_match_restores_the_icon_selected_before()
    {
        var model = Model("MapLayers");
        model.Filter("zzzz");

        model.Filter("map");

        Assert.Equal("MapLayers", model.SelectedName);
    }

    [Fact]
    public void Move_does_nothing_with_no_cells()
    {
        var model = Model();
        model.Filter("zzzz");

        model.Move(IconPaletteMove.Down, 4, 2);

        Assert.Null(model.Selected);
    }

    [Fact]
    public void Right_at_the_last_cell_stays()
    {
        var model = Model("Zoom");

        model.Move(IconPaletteMove.Right, 4, 2);

        Assert.Equal(5, model.Selected);
    }

    [Fact]
    public void Left_at_the_first_cell_stays()
    {
        var model = Model();

        model.Move(IconPaletteMove.Left, 4, 2);

        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void Left_and_Right_move_by_one()
    {
        var model = Model();

        model.Move(IconPaletteMove.Right, 4, 2);
        model.Move(IconPaletteMove.Right, 4, 2);
        model.Move(IconPaletteMove.Left, 4, 2);

        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void Down_moves_by_columns_and_clamps_to_the_last_cell()
    {
        var model = Model();

        model.Move(IconPaletteMove.Down, 4, 2);
        Assert.Equal(4, model.Selected);

        model.Move(IconPaletteMove.Down, 4, 2);
        Assert.Equal(5, model.Selected);
    }

    [Fact]
    public void Up_moves_by_columns_and_stays_in_the_first_row()
    {
        var model = Model("Zoom");

        model.Move(IconPaletteMove.Up, 4, 2);
        Assert.Equal(1, model.Selected);

        model.Move(IconPaletteMove.Up, 4, 2);
        Assert.Equal(1, model.Selected);
    }

    [Fact]
    public void PageDown_moves_by_columns_times_rows_and_PageUp_back()
    {
        var model = Model();

        model.Move(IconPaletteMove.PageDown, 2, 2);
        Assert.Equal(4, model.Selected);

        model.Move(IconPaletteMove.PageUp, 2, 2);
        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void PageDown_clamps_to_the_last_cell()
    {
        var model = Model();

        model.Move(IconPaletteMove.PageDown, 4, 5);

        Assert.Equal(5, model.Selected);
    }

    [Fact]
    public void Home_and_End_go_to_the_first_and_last_cell()
    {
        var model = Model("MapPin");

        model.Move(IconPaletteMove.End, 4, 2);
        Assert.Equal(5, model.Selected);

        model.Move(IconPaletteMove.Home, 4, 2);
        Assert.Equal(0, model.Selected);
    }

    [Fact]
    public void Columns_and_rows_below_one_count_as_one()
    {
        var model = Model();

        model.Move(IconPaletteMove.Down, 0, 0);
        Assert.Equal(1, model.Selected);

        model.Move(IconPaletteMove.PageDown, -3, -3);
        Assert.Equal(2, model.Selected);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    public void Select_sets_the_index(int index, int expected)
    {
        var model = Model();

        model.Select(index);

        Assert.Equal(expected, model.Selected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Select_ignores_an_index_out_of_range(int index)
    {
        var model = Model("MapPin");

        model.Select(index);

        Assert.Equal(2, model.Selected);
    }
}
