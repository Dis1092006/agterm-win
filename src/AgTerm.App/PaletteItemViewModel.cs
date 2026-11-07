using System.Collections.ObjectModel;
using System.ComponentModel;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.App;

/// <summary>The leading mark of a membership editor row (upstream PaletteMark, a2552826).</summary>
public enum PaletteMark
{
    None,
    Flag,
    Focus,
}

public sealed class PaletteItemViewModel : INotifyPropertyChanged
{
    // the sidebar's flag and grid paths (SidebarView.xaml), so the palette mark reads like the sidebar
    private const string FlagData = "M3.5,0.5 V14.5 M3.5,1.5 H13 L10.5,5 L13,8.5 H3.5 Z";
    private const string GridData = "M1.5,1.5 H6.5 V6.5 H1.5 Z M9.5,1.5 H14.5 V6.5 H9.5 Z M1.5,8.5 H6.5 V13.5 H1.5 Z M9.5,8.5 H14.5 V13.5 H9.5 Z";

    private AgentIndicator _indicator;
    private AppSettings _settings = new();
    private bool _hasIndicator;
    private bool _markOn;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PaletteItemViewModel(
        string id,
        string title,
        string? subtitle = null,
        string? shortcut = null,
        string? badge = null,
        AgentStatus? status = null,
        string? statusColor = null,
        StatusShape? statusShape = null,
        PaletteMark mark = PaletteMark.None,
        bool markOn = false)
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
        Mark = mark;
        _markOn = mark != PaletteMark.None && markOn;

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

    public PaletteMark Mark { get; }

    public bool HasMark => Mark != PaletteMark.None;

    /// <summary>The live membership the mark shows; a toggle keeps the row, so it changes in place.</summary>
    public bool MarkOn => _markOn;

    // the sidebar draws a flagged session filled and a focused workspace heavier (upstream markImage:
    // flag.fill, and square.grid.2x2 at .black weight)
    public bool MarkFilled => Mark == PaletteMark.Flag && _markOn;

    public bool MarkOutlined => HasMark && !_markOn;

    public double MarkStrokeThickness => SidebarWorkspaceIcon.GridStrokeThickness(Mark == PaletteMark.Focus && _markOn);

    // x:Bind parses this string into the Path's Geometry even while the mark is collapsed, and a null
    // there fails XAML with E_INVALIDARG when the rows are torn down, which ends the process (a pick
    // cancelled by agtermctl did it)
    private const string NoMarkData = "M0,0";

    public string MarkData => Mark switch
    {
        PaletteMark.Flag => FlagData,
        PaletteMark.Focus => GridData,
        _ => NoMarkData,
    };

    public string? MarkValue => Mark switch
    {
        PaletteMark.Flag => _markOn ? "flagged" : "unflagged",
        PaletteMark.Focus => _markOn ? "focused" : "unfocused",
        _ => null,
    };

    public void SetMark(bool on)
    {
        if (!HasMark || _markOn == on) return;
        _markOn = on;
        foreach (var name in new[] { nameof(MarkOn), nameof(MarkFilled), nameof(MarkOutlined),
                     nameof(MarkStrokeThickness), nameof(MarkValue), nameof(AutomationName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public IReadOnlyList<string> SearchKeys { get; }

    public string AutomationName => string.Join(", ", new[]
    {
        Title, MarkValue, Subtitle,
        HasStatusGlyph ? $"{Status!.Value.ToString().ToLowerInvariant()} status" : null,
        Badge, Shortcut,
    }.Where(value => value is not null));
}