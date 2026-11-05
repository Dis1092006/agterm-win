namespace AgTerm.Xterm.Tests;

public sealed class BitmapSilhouetteTests
{
    [Fact]
    public void Every_pixel_takes_the_ink_and_keeps_its_coverage()
    {
        // premultiplied BGRA: opaque blue, half-covered white, transparent
        byte[] pixels = [0xFF, 0x00, 0x00, 0xFF, 0x80, 0x80, 0x80, 0x80, 0x00, 0x00, 0x00, 0x00];

        Assert.True(BitmapSilhouette.Recolor(pixels, "#ff8000"));

        Assert.Equal(new byte[] { 0x00, 0x80, 0xFF, 0xFF, 0x00, 0x40, 0x80, 0x80, 0x00, 0x00, 0x00, 0x00 }, pixels);
    }

    [Fact]
    public void An_ink_that_is_not_a_color_leaves_the_pixels_alone()
    {
        byte[] pixels = [1, 2, 3, 4];

        Assert.False(BitmapSilhouette.Recolor(pixels, "red"));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, pixels);
    }
}
