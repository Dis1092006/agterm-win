using System.Collections.ObjectModel;
using System.ComponentModel;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.App;

public sealed class PaletteItemViewModel : INotifyPropertyChanged
{
    private AgentIndicator _indicator;
    private AppSettings _settings = new();
    private bool _hasIndicator;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PaletteItemViewModel(
        string id,
        string title,
        string? subtitle = null,
        string? shortcut = null,
        string? badge = null,
        AgentStatus? status = null,
        string? statusColor = null,
        StatusShape? statusShape = null)
    {
        // Pick rows come from the caller unchecked, so a blank id or title must not throw here.
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);

        Id = id;
        Title = title;
        Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle;
        Shortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut;
        Badge = string.IsNullOrWhiteSpace(badge) ? null : badge;
        _hasIndicator = status is not null;
        _indicator = new AgentIndicator(status ?? AgentStatus.Idle, Color: statusColor, Shape: statusShape);

        var searchKeys = Subtitle is null ? new[] { Title } : new[] { Title, Subtitle };
        SearchKeys = new ReadOnlyCollection<string>(searchKeys);
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>The row's position in the caller's pick list; null for built-in rows.</summary>
    public int? SourceIndex { get; init; }

    /// <summary>The trimmed query a "Use ..." row answers with; null for every other row.</summary>
    public string? CustomAnswer { get; private init; }

    public bool IsCustomAnswer => CustomAnswer is not null;

    public IReadOnlyList<string> TitleKeys => new[] { Title };

    public static PaletteItemViewModel ForCustomAnswer(string query) =>
        new("custom-answer", $"Use \"{query}\"") { CustomAnswer = query };

    public string? Subtitle { get; }

    public string? Shortcut { get; }

    public string? Badge { get; }

    public AgentStatus? Status => _hasIndicator ? _indicator.Status : null;

    public string? StatusColor => _hasIndicator ? StatusGlyphPresentation.Color(_indicator, _settings) : null;

    public StatusShape? StatusShape => _hasIndicator ? StatusGlyphPresentation.Shape(_indicator, _settings) : null;

    public string GlyphData => StatusGlyphPresentation.Data(StatusShape ?? Core.Model.StatusShape.Circle);

    public bool HasStatusGlyph => Status is not null and not AgentStatus.Idle;

    public void RefreshIndicator(AgentIndicator? indicator, AppSettings settings)
    {
        var next = indicator ?? _indicator;
        if (next == _indicator && settings == _settings && (_hasIndicator || indicator is null)) return;
        _indicator = next;
        _hasIndicator |= indicator is not null;
        _settings = settings;
        foreach (var name in new[] { nameof(Status), nameof(StatusColor), nameof(StatusShape),
                     nameof(GlyphData), nameof(HasStatusGlyph), nameof(AutomationName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public IReadOnlyList<string> SearchKeys { get; }

    public string AutomationName => string.Join(", ", new[]
    {
        Title, Subtitle,
        HasStatusGlyph ? $"{Status!.Value.ToString().ToLowerInvariant()} status" : null,
        Badge, Shortcut,
    }.Where(value => value is not null));
}