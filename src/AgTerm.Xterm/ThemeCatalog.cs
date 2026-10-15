using System.Collections.ObjectModel;
using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

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
                ? TerminalTheme.IsDarkColor(rendererSettings.BackgroundColor)
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
            "#999999",
            "#5b5b5b",
            "#dfdfff",
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
            "#f5f5f5",
            "#0078d480",
            "#0c0c0c",
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
            "#282a36",
            "#44475a",
            "#ffffff",
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
}