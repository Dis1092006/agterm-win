using System.Text.Json;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

public class CommandTests
{
    [Fact]
    public void AllWireStringsFromParitySurfaceRoundTrip()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/surface.json")));
        var wires = doc.RootElement.GetProperty("control").GetProperty("commands")
            .EnumerateArray().Select(c => c.GetProperty("wire").GetString()!).ToList();

        Assert.Equal(66, wires.Count);
        foreach (var wire in wires)
        {
            var cmd = JsonSerializer.Deserialize<Command>($"\"{wire}\"", ControlJson.Options);
            var encoded = JsonSerializer.Serialize(cmd, ControlJson.Options);
            Assert.Equal($"\"{wire}\"", encoded);
        }
    }

    [Fact]
    public void EnumCoversExactlyTheSurfaceCommandSet()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/surface.json")));
        var wires = doc.RootElement.GetProperty("control").GetProperty("commands")
            .EnumerateArray().Select(c => c.GetProperty("wire").GetString()!)
            .OrderBy(w => w, StringComparer.Ordinal).ToList();

        var enumWires = Enum.GetValues<Command>()
            .Select(c => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(c, ControlJson.Options))!)
            .OrderBy(w => w, StringComparer.Ordinal).ToList();

        Assert.Equal(wires, enumWires);
    }

    [Theory]
    [InlineData("tree", Command.Tree)]
    [InlineData("events.read", Command.EventsRead)]
    [InlineData("session.selectall", Command.SessionSelectAll)]
    [InlineData("session.overlay.result", Command.SessionOverlayResult)]
    [InlineData("debug.appearance", Command.DebugAppearance)]
    public void KnownWireStringsDecode(string wire, Command expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<Command>($"\"{wire}\"", ControlJson.Options));
    }

    [Fact]
    public void UnknownCommandFailsToDecode()
    {
        // Mirrors Swift: an unknown raw value fails decoding; the server maps that to an
        // "unknown command" error instead of crashing.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Command>("\"session.explode\"", ControlJson.Options));
    }
}
