using System.Text.Json;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

public class ControlArgsTests
{
    private static ControlArgs AllFieldsSet() => new()
    {
        Name = "s", Cwd = "s", Targets = new[] { "a", "b" }, Workspace = "s", WorkspaceName = "s",
        CreateWorkspace = true, Collapsed = true, NoSelect = true, Text = "s", Select = true,
        Mode = "s", Path = "s", Color = "s", Shape = "s", Opacity = 0.5, Fit = "s", Position = "s",
        Repeats = true, Pane = "s", PaneId = "s", Ratio = 0.5, RatioDelta = 0.5, All = true,
        Lines = 3, To = "s", After = "s", Before = "s", Run = "s", Kinds = new[] { "a" }, Limit = 3,
        Title = "s", Body = "s", Command = "s", Wait = true, SizePercent = 3, Full = true,
        Follow = true, Window = "s", Width = 3, Height = 3, X = 3, Y = 3, Display = 3,
        Status = "s", Blink = true, AutoReset = true, Sound = "s", Light = "s", Dark = "s",
        Close = true, FontSize = 12.5, AutoSize = true, Mru = true,
    };

    [Fact]
    public void EmptyArgsSerializeToEmptyObject()
    {
        Assert.Equal("{}", ControlJson.Serialize(new ControlArgs()));
    }

    [Fact]
    public void NullFieldsAreOmittedNotEmittedAsNull()
    {
        Assert.Equal("{\"name\":\"api\"}", ControlJson.Serialize(new ControlArgs { Name = "api" }));
    }

    [Fact]
    public void PropertyNamesMatchParitySurfaceByteForByteAndInOrder()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/surface.json")));
        var expected = doc.RootElement.GetProperty("control").GetProperty("args")
            .EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToList();

        using var serialized = JsonDocument.Parse(ControlJson.Serialize(AllFieldsSet()));
        var actual = serialized.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RoundTripPreservesEveryField()
    {
        var json = ControlJson.Serialize(AllFieldsSet());
        var back = ControlJson.Deserialize<ControlArgs>(json);
        Assert.Equal(json, ControlJson.Serialize(back));
    }

    [Fact]
    public void UnknownFieldsAreIgnoredOnDecode()
    {
        var args = ControlJson.Deserialize<ControlArgs>("{\"name\":\"x\",\"futureField\":42}");
        Assert.Equal("x", args!.Name);
    }
}
