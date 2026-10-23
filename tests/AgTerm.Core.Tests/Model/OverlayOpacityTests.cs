using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

public sealed class OverlayOpacityTests
{
    [Theory]
    [InlineData(null, true, null)]
    [InlineData(null, false, 1.0)]
    [InlineData(0.0, true, 0.0)]
    [InlineData(0.6, true, 0.6)]
    [InlineData(1.0, true, 1.0)]
    [InlineData(-2.0, true, 0.0)]
    [InlineData(2.0, true, 1.0)]
    [InlineData(0.0, false, 1.0)]
    [InlineData(0.6, false, 1.0)]
    [InlineData(double.NaN, true, null)]
    [InlineData(double.PositiveInfinity, true, null)]
    public void Effective_opacity_preserves_the_theme_default_unless_overridden(
        double? configured, bool advancedEffectsEnabled, double? expected) =>
        Assert.Equal(expected, new AppSettings { OverlayOpacity = configured }
            .EffectiveOverlayOpacity(advancedEffectsEnabled));

    [Fact]
    public void Optional_field_round_trips_without_changing_upstream_fields()
    {
        var settings = ControlJson.Deserialize<AppSettings>("""{"overlayOpacity":0.75,"backgroundOpacity":0.8}""")!;
        Assert.Equal(0.75, settings.OverlayOpacity);
        Assert.Equal(0.8, settings.BackgroundOpacity);
        Assert.Contains("\"overlayOpacity\":0.75", ControlJson.Serialize(settings));
        Assert.DoesNotContain("overlayOpacity", ControlJson.Serialize(new AppSettings()));
        Assert.DoesNotContain("effectiveOverlayOpacity", ControlJson.Serialize(settings));
        Assert.NotEqual(new AppSettings(), new AppSettings { OverlayOpacity = 0.75 });
    }

    [Theory]
    [InlineData("""{"overlayOpacity":null,"fontSize":15}""", null, null, 15.0)]
    [InlineData("""{"overlayOpacity":-2,"fontSize":15}""", -2.0, 0.0, 15.0)]
    [InlineData("""{"overlayOpacity":2,"fontSize":15}""", 2.0, 1.0, 15.0)]
    [InlineData("""{"overlayOpacity":"bad","fontSize":15}""", null, null, null)]
    [InlineData("""{"overlayOpacity":1e999,"fontSize":15}""", null, null, null)]
    public void Store_uses_existing_recovery_and_keeps_raw_numbers(
        string json, double? raw, double? effective, double? fontSize)
    {
        var directory = Directory.CreateTempSubdirectory("agterm-overlay-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), json);
            var store = new SettingsStore(directory);
            var settings = store.Load();
            Assert.Equal(raw, settings.OverlayOpacity);
            Assert.Equal(effective, settings.EffectiveOverlayOpacity(true));
            Assert.Equal(fontSize, settings.FontSize);
            store.Save(settings);
            Assert.Equal(settings, store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
