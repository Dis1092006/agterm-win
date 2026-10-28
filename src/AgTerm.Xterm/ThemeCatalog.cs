using System.Collections.ObjectModel;
using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed record ActiveTerminalTheme(TerminalTheme? Theme, bool UseDarkChrome);

public sealed record LiveThemePresentation(
    TerminalTheme? Theme,
    bool UseDarkChrome,
    RendererSettingsSnapshot RendererSettings);

public sealed record ThemeCatalogError(string Theme, int Line, string Message);

public static class LiveThemeSettings
{
    public static AppSettings ApplyChange(AppSettings settings, HeadlessThemeUpdate change) =>
        Apply(settings, change.Apply(From(settings)));

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

    /// <summary>The look of one theme over the saved settings, for the palette preview. The theme
    /// slots are ignored, and per-pane font sizes stay, as an appearance flip keeps them.</summary>
    public static LiveThemePresentation Preview(
        AppSettings settings,
        ThemeCatalog catalog,
        string? themeName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        var theme = themeName is null ? null : catalog.Resolve(themeName);
        var rendererSettings = RendererSettingsSnapshot.From(settings, theme, preservePaneFontSizes: true);
        return new LiveThemePresentation(
            theme,
            theme?.IsDark ?? TerminalTheme.IsDarkColor(rendererSettings.BackgroundColor),
            rendererSettings);
    }
}

public sealed class ThemeCatalog
{
    private const string ResourcePrefix = "themes/";

    private static readonly Lazy<ThemeCatalog> Embedded = new(() => FromSources(EmbeddedSources()));

    private readonly IReadOnlyDictionary<string, TerminalTheme> _themes;

    private ThemeCatalog(
        Dictionary<string, TerminalTheme> themes,
        List<ThemeCatalogError> errors)
    {
        _themes = new ReadOnlyDictionary<string, TerminalTheme>(themes);
        Names = Array.AsReadOnly(themes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Errors = errors.AsReadOnly();
    }

    /// <summary>Upstream's theme pack, embedded in this assembly and parsed once on first access.</summary>
    public static ThemeCatalog Default => Embedded.Value;

    public IReadOnlyList<string> Names { get; }

    public IReadOnlyList<ThemeCatalogError> Errors { get; }

    public static ThemeCatalog FromSources(IEnumerable<(string Name, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var themes = new Dictionary<string, TerminalTheme>(StringComparer.Ordinal);
        var errors = new List<ThemeCatalogError>();
        foreach (var (name, text) in sources)
        {
            var result = GhosttyThemeParser.Parse(name, text);
            if (result.Theme is { } theme)
                themes[name] = theme;
            else
                errors.AddRange(result.Errors.Select(error => new ThemeCatalogError(name, error.Line, error.Message)));
        }

        return new ThemeCatalog(themes, errors);
    }

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

    private static IEnumerable<(string Name, string Text)> EmbeddedSources()
    {
        var assembly = typeof(ThemeCatalog).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"theme resource {resource} could not be opened");
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            yield return (resource[ResourcePrefix.Length..], reader.ReadToEnd());
        }
    }
}
