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
}
