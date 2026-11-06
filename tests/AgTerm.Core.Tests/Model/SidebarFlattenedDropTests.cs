using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// SidebarFlattenedDrop.SessionSlot — mapping a flat ListView insertion index (the WinUI sidebar
/// renders one flattened list) back into the (workspace, child index) space SidebarDrop expects.
/// </summary>
public class SidebarFlattenedDropTests
{
    private static readonly Guid W1 = Guid.NewGuid();
    private static readonly Guid W2 = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static SidebarRow Ws(Guid id, bool collapsed = false) =>
        new(SidebarRowKind.Workspace, id, "ws") { Collapsed = collapsed };

    private static SidebarRow S(Guid id) => new(SidebarRowKind.Session, id, "s");

    // [W1, a, b, W2, c]
    private static readonly SidebarRow[] Rows = { Ws(W1), S(A), S(B), Ws(W2), S(C) };

    [Theory]
    [InlineData(1, 0)] // before a → W1 child 0
    [InlineData(2, 1)] // between a and b → W1 child 1
    [InlineData(3, 2)] // between b and the W2 header → append space of W1
    public void Slots_inside_and_after_the_first_workspace_map_to_w1(int flat, int child)
    {
        Assert.Equal((W1, child), SidebarFlattenedDrop.SessionSlot(Rows, flat));
    }

    [Theory]
    [InlineData(4, 0)] // between the W2 header and c → W2 child 0
    [InlineData(5, 1)] // after c (end of list) → W2 child 1
    public void Slots_under_the_second_workspace_map_to_w2(int flat, int child)
    {
        Assert.Equal((W2, child), SidebarFlattenedDrop.SessionSlot(Rows, flat));
    }

    [Fact]
    public void The_top_slot_targets_the_first_workspace_at_zero()
    {
        Assert.Equal((W1, 0), SidebarFlattenedDrop.SessionSlot(Rows, 0));
    }

    [Fact]
    public void A_slot_right_after_a_collapsed_header_inserts_at_its_start()
    {
        var rows = new[] { Ws(W1, collapsed: true), Ws(W2), S(C) };
        Assert.Equal((W1, 0), SidebarFlattenedDrop.SessionSlot(rows, 1));
    }

    [Fact]
    public void An_empty_list_has_no_slot()
    {
        Assert.Null(SidebarFlattenedDrop.SessionSlot(Array.Empty<SidebarRow>(), 0));
    }

    // MARK: - pointer zones: the top quarter of a row is the gap above it, the bottom quarter the gap
    // below it, the middle half a drop ON the row

    private static readonly SidebarRowBounds[] Bounds =
        [new(0, 0, 20), new(1, 20, 20), new(2, 40, 20)];

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(4.9, 0)]
    [InlineData(21.0, 1)]
    [InlineData(56.0, 3)]
    [InlineData(59.9, 3)]
    [InlineData(35.0, 2)]
    public void The_outer_quarters_of_a_row_are_the_gaps_around_it(double y, int gap) =>
        Assert.Equal(new SidebarDropPoint.Gap(gap), SidebarFlattenedDrop.Locate(Bounds, y));

    [Theory]
    [InlineData(5.0, 0)]
    [InlineData(10.0, 0)]
    [InlineData(14.9, 0)]
    [InlineData(25.0, 1)]
    [InlineData(50.0, 2)]
    public void The_middle_half_of_a_row_is_a_drop_on_it(double y, int row) =>
        Assert.Equal(new SidebarDropPoint.OnRow(row), SidebarFlattenedDrop.Locate(Bounds, y));

    [Theory]
    [InlineData(-5.0, 0)]
    [InlineData(80.0, 3)]
    public void A_pointer_outside_every_row_falls_back_to_the_row_midpoints(double y, int gap) =>
        Assert.Equal(new SidebarDropPoint.Gap(gap), SidebarFlattenedDrop.Locate(Bounds, y));

    [Fact]
    public void Unrealized_rows_are_skipped_by_the_midpoint_fallback()
    {
        SidebarRowBounds[] realized = [new(4, 0, 20), new(5, 20, 20)];
        Assert.Equal(new SidebarDropPoint.Gap(6), SidebarFlattenedDrop.Locate(realized, 50));
        Assert.Equal(new SidebarDropPoint.Gap(0), SidebarFlattenedDrop.Locate([], 50));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    [InlineData(5, 1)]
    public void A_gap_in_the_tree_is_a_slot_in_its_workspace(int flat, int slot)
    {
        var workspace = flat < 4 ? W1 : W2;
        Assert.Equal(new SidebarSessionDrop.Gap(workspace, slot),
                     SidebarFlattenedDrop.SessionDrop(Rows, rendersWorkspaceRows: true, new SidebarDropPoint.Gap(flat)));
    }

    [Fact]
    public void A_drop_on_a_tree_row_names_the_row()
    {
        Assert.Equal(new SidebarSessionDrop.OnWorkspace(W2),
                     SidebarFlattenedDrop.SessionDrop(Rows, true, new SidebarDropPoint.OnRow(3)));
        Assert.Equal(new SidebarSessionDrop.OnSession(B),
                     SidebarFlattenedDrop.SessionDrop(Rows, true, new SidebarDropPoint.OnRow(2)));
    }

    [Fact]
    public void The_flagged_list_takes_the_flat_slot_and_the_row()
    {
        SidebarRow[] list = [S(A), S(B), S(C)];
        Assert.Equal(new SidebarSessionDrop.ListGap(2),
                     SidebarFlattenedDrop.SessionDrop(list, rendersWorkspaceRows: false, new SidebarDropPoint.Gap(2)));
        Assert.Equal(new SidebarSessionDrop.OnSession(C),
                     SidebarFlattenedDrop.SessionDrop(list, false, new SidebarDropPoint.OnRow(2)));
    }

    [Fact]
    public void A_tree_without_workspace_rows_has_no_drop() =>
        Assert.Null(SidebarFlattenedDrop.SessionDrop([], true, new SidebarDropPoint.Gap(0)));
}
