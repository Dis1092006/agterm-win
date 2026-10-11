using System.Text.Json;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Protocol;

public class CommandTests
{
    [Fact]
    public void AllWireStringsFromPinAndAdoptedRoundTrip()
    {
        var wires = ParitySurface.Pin("commands").Concat(ParitySurface.Adopted("commands")).ToList();

        Assert.Equal(72, wires.Count);
        foreach (var wire in wires)
        {
            var cmd = JsonSerializer.Deserialize<Command>($"\"{wire}\"", ControlJson.Options);
            Assert.Equal($"\"{wire}\"", JsonSerializer.Serialize(cmd, ControlJson.Options));
        }
    }

    [Fact]
    public void EnumCoversExactlyPinPlusAdoptedCommands()
    {
        var expected = ParitySurface.Pin("commands").Concat(ParitySurface.Adopted("commands"))
            .OrderBy(w => w, StringComparer.Ordinal).ToList();
        var enumWires = Enum.GetValues<Command>()
            .Select(c => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(c, ControlJson.Options))!)
            .OrderBy(w => w, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, enumWires);
    }

    [Fact]
    public void EveryAdoptedCommandExistsUpstream()
    {
        Assert.All(ParitySurface.Adopted("commands"), wire => Assert.Contains(wire, ParitySurface.Upstream("commands")));
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
