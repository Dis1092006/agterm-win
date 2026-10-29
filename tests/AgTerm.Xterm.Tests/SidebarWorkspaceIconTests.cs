namespace AgTerm.Xterm.Tests;

public sealed class SidebarWorkspaceIconTests
{
    [Fact]
    public void A_catalog_name_with_a_glyph_gives_the_glyph_text()
    {
        Assert.Equal("", SidebarWorkspaceIcon.Glyph("MapPin", _ => true));
    }

    [Fact]
    public void A_glyph_the_fonts_lack_gives_null_so_the_row_draws_the_grid()
    {
        Assert.Null(SidebarWorkspaceIcon.Glyph("MapPin", _ => false));
    }

    [Fact]
    public void A_name_the_catalog_lacks_gives_null()
    {
        Assert.Null(SidebarWorkspaceIcon.Glyph("NoSuchIcon", _ => true));
    }

    [Fact]
    public void No_name_gives_null_without_asking_the_catalog()
    {
        Assert.Null(SidebarWorkspaceIcon.Glyph(null, _ => true));
    }

    [Fact]
    public void The_probe_is_asked_for_the_code_point_of_the_name()
    {
        var asked = new List<int>();

        SidebarWorkspaceIcon.Glyph("MapPin", codePoint => { asked.Add(codePoint); return true; });

        Assert.Equal([0xE707], asked);
    }
}
