using AgTerm.Core.Icons;

namespace AgTerm.Core.Tests.Icons;

public sealed class SegoeIconCatalogTests
{
    [Fact]
    public void The_catalog_has_every_icon_of_the_list_in_code_point_order()
    {
        var all = SegoeIconCatalog.All;

        Assert.Equal(1533, all.Count);
        Assert.Equal(new SegoeIcon("GlobalNavButton", 0xE700), all[0]);
        for (var i = 1; i < all.Count; i++)
            Assert.True(all[i].CodePoint > all[i - 1].CodePoint, all[i].Name);
    }

    [Fact]
    public void Names_are_unique()
    {
        var names = SegoeIconCatalog.All.Select(i => i.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_name_resolves_to_its_code_point()
    {
        Assert.True(SegoeIconCatalog.TryGet("MapPin", out var icon));
        Assert.Equal(0xE707, icon.CodePoint);
    }

    [Fact]
    public void A_name_in_another_case_is_unknown()
    {
        Assert.False(SegoeIconCatalog.TryGet("mappin", out _));
    }
}
