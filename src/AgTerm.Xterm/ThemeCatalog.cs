using System.Collections.ObjectModel;
using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed record TerminalTheme(
    string Name,
    string ForegroundColor,
    string BackgroundColor,
    string CursorColor,
    string SelectionBackgroundColor,
    IReadOnlyList<string> AnsiColors)
{
    public bool IsDark => ThemeCatalog.IsDark(BackgroundColor);
}

public sealed record ActiveTerminalTheme(TerminalTheme? Theme, bool UseDarkChrome);

public sealed record LiveThemePresentation(
    TerminalTheme? Theme,
    bool UseDarkChrome,
    RendererSettingsSnapshot RendererSettings);

public static class LiveThemeSettings
{
    public static HeadlessThemeSnapshot From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var sync = settings.FollowSystemAppearance == true && settings.DarkTheme is not null;
        return new HeadlessThemeSnapshot(
            settings.Theme,
            sync ? settings.DarkTheme : null,
            sync);
    }

    public static AppSettings Apply(AppSettings settings, HeadlessThemeSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);

        return settings with
        {
            Theme = state.Light,
            DarkTheme = state.Dark,
            FollowSystemAppearance = state.Sync,
        };
    }

    public static LiveThemePresentation Resolve(
        AppSettings settings,
        ThemeCatalog catalog,
        bool systemDark,
        bool preservePaneFontSizes = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        var state = From(settings);
        var active = catalog.ResolveActive(
            state.Light,
            state.Dark,
            state.Sync,
            systemDark);
        var rendererSettings = RendererSettingsSnapshot.From(
            settings,
            active.Theme,
            preservePaneFontSizes);
        return new LiveThemePresentation(
            active.Theme,
            active.Theme is null
                ? ThemeCatalog.IsDark(rendererSettings.BackgroundColor)
                : active.UseDarkChrome,
            rendererSettings);
    }
}

public sealed class ThemeCatalog
{
    private readonly IReadOnlyDictionary<string, TerminalTheme> _themes;

    private ThemeCatalog(IEnumerable<TerminalTheme> themes)
    {
        _themes = new ReadOnlyDictionary<string, TerminalTheme>(
            themes.ToDictionary(theme => theme.Name, StringComparer.Ordinal));
        Names = Array.AsReadOnly(
            _themes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static ThemeCatalog Default { get; } = new(
    [
        new TerminalTheme(
            "agterm",
            "#ffffff",
            "#303030",
            "#d6d6d6",
            "#5b5b5b",
            Array.AsReadOnly(
            [
                "#353535", "#d25252", "#a5c261", "#ffc66d",
                "#2774c7", "#d197d9", "#bed6ff", "#eeeeec",
                "#6a6a6a", "#c96363", "#c2e075", "#e1e48b",
                "#8ab7d9", "#efb5f7", "#dcf4ff", "#ffffff",
            ])),
        new TerminalTheme(
            "Builtin Light",
            "#0c0c0c",
            "#f5f5f5",
            "#0c0c0c",
            "#0078d480",
            Array.AsReadOnly(
            [
                "#0c0c0c", "#c50f1f", "#13a10e", "#c19c00",
                "#0037da", "#881798", "#3a96dd", "#cccccc",
                "#767676", "#e74856", "#16c60c", "#f9f1a5",
                "#3b78ff", "#b4009e", "#61d6d6", "#f2f2f2",
            ])),
        new TerminalTheme(
            "Dracula",
            "#f8f8f2",
            "#282a36",
            "#f8f8f2",
            "#44475a",
            Array.AsReadOnly(
            [
                "#21222c", "#ff5555", "#50fa7b", "#f1fa8c",
                "#bd93f9", "#ff79c6", "#8be9fd", "#f8f8f2",
                "#6272a4", "#ff6e6e", "#69ff94", "#ffffa5",
                "#d6acff", "#ff92df", "#a4ffff", "#ffffff",
            ]))
    ]);

    public IReadOnlyList<string> Names { get; }

    public TerminalTheme? Resolve(string name) =>
        _themes.TryGetValue(name, out var theme) ? theme : null;

    public ActiveTerminalTheme ResolveActive(
        string? light,
        string? dark,
        bool sync,
        bool systemDark)
    {
        var name = sync && systemDark ? dark : light;
        var theme = name is null ? null : Resolve(name);
        return new ActiveTerminalTheme(theme, theme?.IsDark ?? systemDark);
    }

    internal static bool IsDark(string color)
    {
        if (color.Length != 7 || color[0] != '#')
            return true;
        if (!byte.TryParse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(color.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(color.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return true;
        }

        var luminance = 0.299 * red / 255.0 + 0.587 * green / 255.0 + 0.114 * blue / 255.0;
        return luminance < 0.5;
    }
}