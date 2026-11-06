using System.Text.Json;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// Ported from upstream SidebarTintTests (f3fea57b).
public class SidebarTintTests
{
    [Fact]
    public void PartsAreOrderedAndNeverEmpty()
    {
        Assert.Equal(new[] { "icon", "name" }, new SidebarTint(TintColor.Red, TintParts.Name | TintParts.Icon).PartNames);
        Assert.Equal(TintParts.Icon, new SidebarTint(TintColor.Red, TintParts.None).Parts);
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name | TintParts.Icon),
            new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Name));
        Assert.True(new SidebarTint(TintColor.Red, TintParts.Name).Shows(TintParts.Name));
        Assert.False(new SidebarTint(TintColor.Red, TintParts.Name).Shows(TintParts.Icon));
    }

    [Theory]
    [InlineData(new[] { "icon" }, TintParts.Icon)]
    [InlineData(new[] { "name", "icon" }, TintParts.Icon | TintParts.Name)]
    [InlineData(new[] { " Name " }, TintParts.Name)]
    [InlineData(new[] { "icon", "icon" }, TintParts.Icon)]
    public void ParsePartsAcceptsKnownParts(string[] raw, TintParts expected)
    {
        Assert.Equal(expected, SidebarTint.ParseParts(raw));
    }

    // raw is the list joined with "|"; null is the empty list
    [Theory]
    [InlineData(null)]
    [InlineData("strip")]
    [InlineData("icon|")]
    [InlineData("icon|both")]
    [InlineData("icon\n")]
    public void ParsePartsRejectsEmptyOrUnknown(string? raw)
    {
        Assert.Null(SidebarTint.ParseParts(raw is null ? [] : raw.Split('|')));
    }

    [Fact]
    public void RecolorKeepsPartsAndStartsUntintedOnTheIcon()
    {
        var named = new SidebarTint(TintColor.Green, TintParts.Name);
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name), SidebarTint.Recolor(named, TintColor.Red));
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Icon), SidebarTint.Recolor(null, TintColor.Red));
        Assert.Null(SidebarTint.Recolor(named, null));
    }

    [Fact]
    public void JsonRoundTripsColorAndParts()
    {
        var tint = new SidebarTint(TintColor.Purple, TintParts.Icon | TintParts.Name);
        var json = ControlJson.Serialize(tint);
        Assert.Equal("{\"color\":\"purple\",\"on\":[\"icon\",\"name\"]}", json);
        Assert.Equal(tint, ControlJson.Deserialize<SidebarTint>(json));
    }

    [Fact]
    public void DecodeSkipsUnknownPartsAndDefaultsToTheIcon()
    {
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name),
            ControlJson.Deserialize<SidebarTint>("{\"color\":\"red\",\"on\":[\"strip\",\"name\"]}"));
        Assert.Equal(new SidebarTint(TintColor.Red),
            ControlJson.Deserialize<SidebarTint>("{\"color\":\"red\"}"));
        Assert.Equal(new SidebarTint(TintColor.Red),
            ControlJson.Deserialize<SidebarTint>("{\"color\":\"red\",\"on\":\"icon\"}"));
    }

    [Theory]
    [InlineData("{\"color\":\"teal\",\"on\":[\"icon\"]}")]
    [InlineData("{\"color\":\"Red\"}")]
    [InlineData("{\"on\":[\"icon\"]}")]
    [InlineData("\"red\"")]
    public void DecodeThrowsOnAnUnknownColor(string json)
    {
        Assert.Throws<JsonException>(() => ControlJson.Deserialize<SidebarTint>(json));
    }

    [Fact]
    public void UpdateResolvesAgainstTheCurrentTint()
    {
        var named = new SidebarTint(TintColor.Green, TintParts.Name);
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name),
            ControlTintUpdate.Set(TintColor.Red, null).Resolved(named));
        Assert.Equal(new SidebarTint(TintColor.Red), ControlTintUpdate.Set(TintColor.Red, null).Resolved(null));
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Icon | TintParts.Name),
            ControlTintUpdate.Set(TintColor.Red, TintParts.Icon | TintParts.Name).Resolved(named));
        Assert.Null(ControlTintUpdate.Clear.Resolved(named));
    }

    // Windows-only: flag is a third part, after icon and name, and only a session row has it.
    [Fact]
    public void FlagIsTheThirdPartOfASessionTint()
    {
        var all = new SidebarTint(TintColor.Red, TintParts.Flag | TintParts.Name | TintParts.Icon);

        Assert.Equal(["icon", "name", "flag"], all.PartNames);
        Assert.True(new SidebarTint(TintColor.Red, TintParts.Flag).Shows(TintParts.Flag));
        Assert.Equal(TintParts.Flag | TintParts.Icon, SidebarTint.ParseParts([" Flag ", "icon"]));
        Assert.Equal("{\"color\":\"red\",\"on\":[\"icon\",\"name\",\"flag\"]}", ControlJson.Serialize(all));
        Assert.Equal(all, ControlJson.Deserialize<SidebarTint>("{\"color\":\"red\",\"on\":[\"flag\",\"name\",\"icon\"]}"));
    }

    [Fact]
    public void AWorkspacePlacementRejectsTheFlag()
    {
        Assert.Equal(TintParts.Icon | TintParts.Name, SidebarTint.ParseParts(["name", "icon"], SidebarTint.WorkspaceParts));
        Assert.Null(SidebarTint.ParseParts(["icon", "flag"], SidebarTint.WorkspaceParts));
    }

    [Fact]
    public void AWorkspaceTintDropsTheFlag()
    {
        Assert.Equal(new SidebarTint(TintColor.Red, TintParts.Name),
            SidebarTint.ForWorkspace(new SidebarTint(TintColor.Red, TintParts.Name | TintParts.Flag)));
        Assert.Equal(new SidebarTint(TintColor.Red), SidebarTint.ForWorkspace(new SidebarTint(TintColor.Red, TintParts.Flag)));
        Assert.Null(SidebarTint.ForWorkspace(null));
    }

    [Fact]
    public void WithPartAddsOrRemovesOnePartAndClearsAnEmptiedTint()
    {
        var icon = new SidebarTint(TintColor.Blue);

        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Icon | TintParts.Flag), SidebarTint.WithPart(icon, TintParts.Flag, true));
        Assert.Equal(icon, SidebarTint.WithPart(icon, TintParts.Icon, true));
        Assert.Equal(icon, SidebarTint.WithPart(new SidebarTint(TintColor.Blue, TintParts.Icon | TintParts.Name), TintParts.Name, false));
        Assert.Null(SidebarTint.WithPart(icon, TintParts.Icon, false));
        Assert.Null(SidebarTint.WithPart(null, TintParts.Name, true));
    }

    [Fact]
    public void ColorNamesAreTheWireSpelling()
    {
        Assert.Equal("red|orange|yellow|green|blue|purple|gray", TintColors.ValidNamesList);
        Assert.Equal("icon,name,flag", SidebarTint.ValidPartNamesList);
        Assert.Equal("icon,name", SidebarTint.WorkspacePartNamesList);
        Assert.True(TintColors.TryParse("orange", out var orange));
        Assert.Equal(TintColor.Orange, orange);
        Assert.False(TintColors.TryParse("Orange", out _));
        Assert.Equal("gray", TintColors.Wire(TintColor.Gray));
    }
}
