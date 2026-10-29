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

    [Fact]
    public void An_image_path_shows_the_image()
    {
        Assert.Equal(SidebarWorkspaceIconKind.Image,
            SidebarWorkspaceIcon.KindOf(@"C:\site\favicon.ico", _ => true, imageFailed: false));
    }

    [Fact]
    public void An_image_that_failed_to_load_shows_the_grid()
    {
        Assert.Equal(SidebarWorkspaceIconKind.Grid,
            SidebarWorkspaceIcon.KindOf(@"C:\site\favicon.ico", _ => true, imageFailed: true));
    }

    [Fact]
    public void A_catalog_name_with_a_glyph_shows_the_glyph()
    {
        Assert.Equal(SidebarWorkspaceIconKind.Glyph, SidebarWorkspaceIcon.KindOf("MapPin", _ => true, imageFailed: false));
    }

    [Fact]
    public void No_icon_or_a_missing_glyph_shows_the_grid()
    {
        Assert.Equal(SidebarWorkspaceIconKind.Grid, SidebarWorkspaceIcon.KindOf(null, _ => true, imageFailed: false));
        Assert.Equal(SidebarWorkspaceIconKind.Grid, SidebarWorkspaceIcon.KindOf("MapPin", _ => false, imageFailed: false));
    }

    [Fact]
    public void Only_an_image_path_gives_an_image_file()
    {
        Assert.Equal(@"C:\site\logo.svg", SidebarWorkspaceIcon.ImageFile(@"C:\site\logo.svg"));
        Assert.Null(SidebarWorkspaceIcon.ImageFile("MapPin"));
        Assert.Null(SidebarWorkspaceIcon.ImageFile(null));
    }

    [Fact]
    public void An_image_path_gives_no_glyph()
    {
        Assert.Null(SidebarWorkspaceIcon.Glyph(@"C:\site\favicon.ico", _ => true));
    }
}
