using System.Text.Json;
using AgTerm.Core.Keymap;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Keymap;

public class BuiltinActionTests
{
    private static List<(string Wire, string? DefaultChord)> SurfaceActions()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.File("parity/surface.json")));
        return doc.RootElement.GetProperty("keymap").GetProperty("actions")
            .EnumerateArray()
            .Select(a => (a.GetProperty("wire").GetString()!,
                a.GetProperty("defaultChord").ValueKind == JsonValueKind.Null
                    ? null
                    : a.GetProperty("defaultChord").GetString()))
            .ToList();
    }

    [Fact]
    public void ActionSetMatchesParitySurface()
    {
        var expected = SurfaceActions().Select(a => a.Wire).OrderBy(w => w, StringComparer.Ordinal);
        var actual = BuiltinActions.All.Select(a => a.WireName()).OrderBy(w => w, StringComparer.Ordinal);
        Assert.Equal(expected, actual);
        Assert.Equal(41, BuiltinActions.All.Count);
    }

    [Fact]
    public void DefaultChordsMatchParitySurface()
    {
        foreach (var (wire, defaultChord) in SurfaceActions())
        {
            Assert.True(BuiltinActions.TryParse(wire, out var action), $"unparsed action {wire}");
            var chord = action.DefaultChord();

            if (defaultChord is null)
            {
                Assert.Null(chord);
                continue;
            }

            // surface.json spells modifiers in Swift declaration order; compare set-wise.
            var parts = defaultChord.Split('+');
            // "control+`" splits to ["control", "`"]; "command++" to ["command", "", ""] (key "+").
            var key = defaultChord.EndsWith("++", StringComparison.Ordinal) ? "+" : parts[^1];
            var expectedMods = parts[..^1].Where(p => p.Length > 0).Select(p => p switch
            {
                "command" => Modifiers.Command,
                "control" => Modifiers.Control,
                "option" => Modifiers.Option,
                "shift" => Modifiers.Shift,
                _ => throw new InvalidOperationException($"unknown modifier {p} in {defaultChord}"),
            }).Aggregate(Modifiers.None, (acc, m) => acc | m);

            Assert.NotNull(chord);
            Assert.Equal(key, chord!.Value.Key);
            Assert.Equal(expectedMods, chord.Value.Mods);
        }
    }

    [Fact]
    public void ShippedDefaultsAreAllDistinct()
    {
        // The order-independent conflict resolution relies on this upstream invariant.
        var defaults = BuiltinActions.All
            .Select(a => a.DefaultChord()).Where(c => c is not null).Select(c => c!.Value).ToList();
        Assert.Equal(defaults.Count, defaults.Distinct().Count());
    }

    [Fact]
    public void UnknownActionNameDoesNotParse()
    {
        Assert.False(BuiltinActions.TryParse("explode_session", out _));
    }

    [Fact]
    public void KeymapEquivalentPrefersTheOverride()
    {
        var overrideChord = new Chord(Modifiers.Command, "e");
        var keymap = new AgTerm.Core.Keymap.Keymap(
            new Dictionary<BuiltinAction, Chord> { [BuiltinAction.NewSession] = overrideChord },
            new List<CustomCommand>());

        Assert.Equal(overrideChord, keymap.EquivalentFor(BuiltinAction.NewSession));
        Assert.Equal(new Chord(Modifiers.Command, "w"), keymap.EquivalentFor(BuiltinAction.CloseSession));
        Assert.Null(keymap.EquivalentFor(BuiltinAction.RenameSession)); // keyless by default
    }
}
