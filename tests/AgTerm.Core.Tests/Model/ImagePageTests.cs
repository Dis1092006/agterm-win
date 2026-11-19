using System.Text;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The page an image overlay shows and the size it opens at (upstream ImagePage, #844).
public class ImagePageTests
{
    [Theory]
    [InlineData(1, 2, 600, 400)]
    [InlineData(1, 1, 1200, 800)]
    [InlineData(6, 2, 400, 600)]
    [InlineData(8, 1, 800, 1200)]
    [InlineData(3, 1, 1200, 800)]
    [InlineData(1, 0, 1200, 800)]
    public void NaturalSizeIsDisplayPixelsWithRotationApplied(int orientation, double scale, double width, double height)
    {
        var natural = ImagePage.Natural(1200, 800, orientation, scale);
        Assert.Equal(width, natural.Width);
        Assert.Equal(height, natural.Height);
    }

    [Theory]
    [InlineData(600, 400, 1, 600, 400)]
    [InlineData(600, 400, 1.5, 900, 600)]
    [InlineData(100, 50, 1, 240, 120)]
    [InlineData(3000.5, 300, 1, 3001, 300)]
    public void PointsFollowTheZoomAndScaleANarrowPictureUpToTheMinimumWidth(
        double width, double height, double zoom, int expectedWidth, int expectedHeight) =>
        Assert.Equal(new OverlayPointSize(expectedWidth, expectedHeight), ImagePage.Points((width, height), zoom));

    // regression: a picture capped on one axis kept its natural size on the other, leaving empty bands
    [Theory]
    [InlineData(3000, 300, 800, 80)]
    [InlineData(600, 3000, 122, 611)]
    [InlineData(600, 400, 600, 400)]
    [InlineData(1000, 1000, 611, 611)]
    [InlineData(100, 50, 240, 120)]
    public void ThePanelKeepsThePicturesAspectInsideTheCap(double width, double height, int expectedWidth, int expectedHeight) =>
        Assert.Equal(new OverlayPointSize(expectedWidth, expectedHeight),
                     ImagePage.Points((width, height), 1, new HudRect(1000, 800)));

    [Fact]
    public void AnEmptyPictureGetsTheMinimumSquare() =>
        Assert.Equal(new OverlayPointSize(240, 240), ImagePage.Points((0, 400), 1));

    public static TheoryData<byte[], string?> Signatures() => new()
    {
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], "image/png" },
        { [0xFF, 0xD8, 0xFF, 0xE0, 0], "image/jpeg" },
        { Encoding.ASCII.GetBytes("GIF89a.."), "image/gif" },
        { Encoding.ASCII.GetBytes("GIF87a.."), "image/gif" },
        { Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "), "image/webp" },
        { Encoding.ASCII.GetBytes("<svg xmlns="), null },
        { Encoding.ASCII.GetBytes("<html><body>"), null },
        { Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt "), null },
        { Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEB"), null },
        { [0x89, 0x50, 0x4E], null },
        { [], null },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void SignatureNamesOnlyTheFourRasterFormats(byte[] bytes, string? expected) =>
        Assert.Equal(expected, ImagePage.MimeType(bytes));

    [Theory]
    [InlineData(10_000, 10_000, true)]
    [InlineData(10_000, 10_001, false)]
    [InlineData(12_000, 12_000, false)]
    [InlineData(int.MaxValue, 2, false)]
    public void PixelLimitRefusesADecompressionBombsPromisedSize(int width, int height, bool allowed) =>
        Assert.Equal(allowed, ImagePage.AllowsPixels(width, height));

    [Fact]
    public void HtmlShowsTheSourceFillingThePage()
    {
        var html = ImagePage.Html("https://agterm-image.example/image");
        Assert.Contains("src=\"https://agterm-image.example/image\"", html);
        Assert.Contains("width:100%;height:100%;object-fit:contain", html);
    }
}
