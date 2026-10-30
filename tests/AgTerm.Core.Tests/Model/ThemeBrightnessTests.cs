using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// upstream agtermCore/Tests/agtermCoreTests/ThemeBrightnessTests.swift at 7df70fe
public sealed class ThemeBrightnessTests
{
    [Fact]
    public void Pure_black_and_white()
    {
        Assert.True(ThemeBrightness.IsDark(0, 0, 0));
        Assert.False(ThemeBrightness.IsDark(1, 1, 1));
    }

    [Fact]
    public void Real_theme_backgrounds()
    {
        // gruvbox dark hard #1d2021, solarized dark #002b36 → dark
        Assert.True(ThemeBrightness.IsDark(0.114, 0.125, 0.129));
        Assert.True(ThemeBrightness.IsDark(0.0, 0.169, 0.212));
        // solarized light #fdf6e3 → light
        Assert.False(ThemeBrightness.IsDark(0.992, 0.965, 0.890));
    }

    [Fact]
    public void Green_dominates_the_luma_weighting()
    {
        Assert.False(ThemeBrightness.IsDark(0, 1, 0));
        Assert.True(ThemeBrightness.IsDark(0, 0, 1));
        Assert.True(ThemeBrightness.IsDark(1, 0, 0));
    }

    [Fact]
    public void Gray_scale_around_the_midpoint()
    {
        Assert.False(ThemeBrightness.IsDark(0.55, 0.55, 0.55));
        Assert.True(ThemeBrightness.IsDark(0.45, 0.45, 0.45));
    }

    [Fact]
    public void Neutral_shift_matches_the_raw_classifier()
    {
        Assert.True(ThemeBrightness.IsDark(0.114, 0.125, 0.129, shiftAmount: 0));
        Assert.False(ThemeBrightness.IsDark(0.992, 0.965, 0.890, shiftAmount: 0));
    }

    [Fact]
    public void Strong_tint_crosses_the_midpoint()
    {
        // hot dog stand #ea3323: raw luma ~0.41 → dark; the lightest wash (-0.30) → ~0.59 → light
        var (r, g, b) = (0.918, 0.200, 0.137);
        Assert.True(ThemeBrightness.IsDark(r, g, b));
        Assert.False(ThemeBrightness.IsDark(r, g, b, shiftAmount: -0.30));
        // a light gray (~0.55 → light) with the darkest wash (+0.30) → ~0.385 → dark
        Assert.False(ThemeBrightness.IsDark(0.55, 0.55, 0.55));
        Assert.True(ThemeBrightness.IsDark(0.55, 0.55, 0.55, shiftAmount: 0.30));
    }

    [Fact]
    public void Wash_opacity_is_clamped_to_one()
    {
        Assert.False(ThemeBrightness.IsDark(0, 0, 0, shiftAmount: -5));
        Assert.True(ThemeBrightness.IsDark(1, 1, 1, shiftAmount: 5));
    }
}
