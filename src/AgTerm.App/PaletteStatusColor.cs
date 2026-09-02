namespace AgTerm.App;

public static class PaletteStatusColor
{
    public static uint? Resolve(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
            return null;

        return uint.TryParse(
            value.AsSpan(1),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var rgb)
                ? 0xFF000000u | rgb
                : null;
    }
}