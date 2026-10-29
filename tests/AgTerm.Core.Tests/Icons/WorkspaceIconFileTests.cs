using AgTerm.Core.Icons;

namespace AgTerm.Core.Tests.Icons;

// A workspace icon is a Segoe name or a path to an image file. Segoe names are letters and digits
// only, so any other character marks a path.
public sealed class WorkspaceIconFileTests
{
    [Theory]
    [InlineData("MapPin")]
    [InlineData("MapPin2")]
    public void A_segoe_name_is_not_a_file(string value)
    {
        Assert.False(WorkspaceIconFile.IsFile(value));
    }

    [Theory]
    [InlineData("favicon.ico")]
    [InlineData(@".\public\favicon.ico")]
    [InlineData(@"C:\git\site\public\favicon.ico")]
    [InlineData("public/logo.svg")]
    public void A_value_with_any_other_character_is_a_file(string value)
    {
        Assert.True(WorkspaceIconFile.IsFile(value));
    }

    [Theory]
    [InlineData(@"C:\site\favicon.ico")]
    [InlineData(@"C:\site\logo.PNG")]
    [InlineData(@"C:\site\logo.svg")]
    public void An_existing_absolute_ico_png_or_svg_file_is_accepted(string path)
    {
        Assert.Null(WorkspaceIconFile.Error(path, _ => true));
    }

    [Fact]
    public void A_relative_path_is_refused()
    {
        Assert.Equal(@"icon file path must be absolute: public\favicon.ico",
            WorkspaceIconFile.Error(@"public\favicon.ico", _ => true));
    }

    [Fact]
    public void Another_extension_is_refused()
    {
        Assert.Equal(@"unsupported icon file: C:\site\icon.bmp",
            WorkspaceIconFile.Error(@"C:\site\icon.bmp", _ => true));
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        Assert.Equal(@"icon file not found: C:\site\favicon.ico",
            WorkspaceIconFile.Error(@"C:\site\favicon.ico", _ => false));
    }
}
