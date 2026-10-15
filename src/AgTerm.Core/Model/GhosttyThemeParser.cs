using System.Globalization;

namespace AgTerm.Core.Model;

public sealed record GhosttyThemeError(int Line, string Message);

public sealed record GhosttyThemeParseResult(TerminalTheme? Theme, IReadOnlyList<GhosttyThemeError> Errors);

/// <summary>Reads a Ghostty theme file: key = value lines, blank lines and # comments skipped, as
/// upstream's keyValues reader does (agterm/Ghostty/GhosttyApp.swift, ed831ab). Every value a
/// TerminalTheme needs is required, because the bundled pack always has all of them and a gap would
/// otherwise show as a silently wrong color.</summary>
public static class GhosttyThemeParser
{
    private const int PaletteSize = 16;

    private static readonly string[] RequiredKeys =
    [
        "foreground",
        "background",
        "cursor-color",
        "cursor-text",
        "selection-background",
        "selection-foreground",
    ];

    public static GhosttyThemeParseResult Parse(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(text);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var palette = new string?[PaletteSize];
        var errors = new List<GhosttyThemeError>();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            ReadLine(index + 1, lines[index].Trim(), values, palette, errors);

        AddMissing(values, palette, errors);
        if (errors.Count > 0)
            return new GhosttyThemeParseResult(null, errors);

        return new GhosttyThemeParseResult(
            new TerminalTheme(
                name,
                values["foreground"],
                values["background"],
                values["cursor-color"],
                values["cursor-text"],
                values["selection-background"],
                values["selection-foreground"],
                Array.AsReadOnly(palette.Select(color => color!).ToArray())),
            []);
    }

    private static void ReadLine(
        int line,
        string text,
        Dictionary<string, string> values,
        string?[] palette,
        List<GhosttyThemeError> errors)
    {
        if (text.Length == 0 || text[0] == '#')
            return;

        var equals = text.IndexOf('=');
        if (equals < 0)
        {
            errors.Add(new GhosttyThemeError(line, "expected key = value"));
            return;
        }

        var key = text[..equals].Trim();
        var value = text[(equals + 1)..].Trim();
        if (key.Length == 0)
        {
            errors.Add(new GhosttyThemeError(line, "empty key"));
            return;
        }

        if (key == "palette")
        {
            ReadPalette(line, value, palette, errors);
            return;
        }

        // a theme file may carry keys this terminal has no use for
        if (Array.IndexOf(RequiredKeys, key) < 0)
            return;

        if (!IsHexColor(value))
        {
            errors.Add(new GhosttyThemeError(line, $"{key} must be #rrggbb: {value}"));
            return;
        }

        values[key] = value;
    }

    private static void ReadPalette(int line, string value, string?[] palette, List<GhosttyThemeError> errors)
    {
        var equals = value.IndexOf('=');
        if (equals < 0)
        {
            errors.Add(new GhosttyThemeError(line, $"palette must be N=#rrggbb: {value}"));
            return;
        }

        var indexText = value[..equals].Trim();
        var color = value[(equals + 1)..].Trim();
        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index >= PaletteSize)
        {
            errors.Add(new GhosttyThemeError(line, $"palette index must be 0 to 15: {indexText}"));
            return;
        }

        if (!IsHexColor(color))
        {
            errors.Add(new GhosttyThemeError(line, $"palette {index} must be #rrggbb: {color}"));
            return;
        }

        palette[index] = color;
    }

    private static void AddMissing(
        Dictionary<string, string> values,
        string?[] palette,
        List<GhosttyThemeError> errors)
    {
        foreach (var key in RequiredKeys)
        {
            if (!values.ContainsKey(key))
                errors.Add(new GhosttyThemeError(0, $"missing {key}"));
        }

        for (var index = 0; index < PaletteSize; index++)
        {
            if (palette[index] is null)
                errors.Add(new GhosttyThemeError(0, $"missing palette {index}"));
        }
    }

    // Uri.IsHexDigit accepts only ASCII 0-9, a-f and A-F
    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit);
}
