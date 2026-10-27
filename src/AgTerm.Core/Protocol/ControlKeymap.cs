using System.Text.Json.Serialization;
using AgTerm.Core.Keymap;

namespace AgTerm.Core.Protocol;

/// <summary>One rebindable built-in and the Windows chord the keymap resolves it to.</summary>
public sealed record ControlKeymapAction
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("chord")]
    public string? Chord { get; init; }

    [JsonPropertyName("overridden")]
    public bool? Overridden { get; init; }
}

public sealed record ControlKeymapCommand
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; init; }

    // Upstream encodes this as a non-optional Bool, so it is written even when false.
    [JsonPropertyName("repeats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Repeats { get; init; }
}

public sealed record ControlKeymapDiagnostic
{
    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>The keymap.list payload.</summary>
public sealed record ControlKeymap
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("actions")]
    public required ControlKeymapAction[] Actions { get; init; }

    [JsonPropertyName("commands")]
    public required ControlKeymapCommand[] Commands { get; init; }

    [JsonPropertyName("diagnostics")]
    public required ControlKeymapDiagnostic[] Diagnostics { get; init; }
}

public static class ControlKeymapProjection
{
    public static ControlKeymap Project(Keymap.Keymap keymap, IReadOnlyList<KeymapDiagnostic> parserDiagnostics, string path)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(parserDiagnostics);

        return new ControlKeymap
        {
            Path = path,
            Actions = BuiltinActions.All.Select(action => ActionRow(keymap, action)).ToArray(),
            Commands = keymap.Commands
                .Select(c => new ControlKeymapCommand
                {
                    Name = c.Name,
                    Shortcut = c.Shortcut.Length == 0 ? null : c.Shortcut,
                })
                .ToArray(),
            Diagnostics = parserDiagnostics
                .Concat(WindowsChordShadowing.Diagnostics(keymap))
                .Select(d => new ControlKeymapDiagnostic { Line = d.Line, Message = d.Message })
                .ToArray(),
        };
    }

    private static ControlKeymapAction ActionRow(Keymap.Keymap keymap, BuiltinAction action)
    {
        var resolved = keymap.EquivalentFor(action);
        return new ControlKeymapAction
        {
            Action = action.WireName(),
            Chord = resolved is null ? null : WindowsChordShadowing.Spelling(WindowsChordDecoder.Effective(resolved.Value)),
            Overridden = MovedFromShipped(resolved, WindowsDefaultChords.ShippedFor(action)) ? true : null,
        };
    }

    private static bool MovedFromShipped(Chord? resolved, Chord? shipped)
    {
        if (resolved is null || shipped is null)
        {
            return resolved is null != shipped is null;
        }

        return WindowsChordDecoder.Effective(resolved.Value) != WindowsChordDecoder.Effective(shipped.Value);
    }
}
