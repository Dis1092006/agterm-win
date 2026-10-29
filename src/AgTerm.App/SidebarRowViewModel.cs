using System.ComponentModel;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.Text;

namespace AgTerm.App;

/// <summary>
/// One sidebar row's bindable state — a mutable INotifyPropertyChanged wrapper over the immutable
/// Core SidebarRow so the ListView reconciles in place (the upstream reloadItem diff): Update()
/// re-applies a fresh row record and raises change notifications only for what differs.
/// </summary>
public sealed class SidebarRowViewModel : INotifyPropertyChanged
{
    private SidebarRow _row;
    private AppSettings _settings;
    private readonly SidebarMode _mode;
    private SidebarSurfaceStatus _surfaceStatus;
    private SidebarRowLook _look;
    private string? _iconName;
    private ImageSource? _image;
    private bool _imageFailed;

    public SidebarRowViewModel(
        SidebarRow row,
        AppSettings settings,
        SidebarMode mode,
        SidebarSurfaceStatus surfaceStatus = SidebarSurfaceStatus.Ready,
        SidebarRowLook? look = null,
        string? iconName = null)
    {
        _row = row;
        _settings = settings;
        _mode = mode;
        _surfaceStatus = surfaceStatus;
        _look = look ?? SidebarRowLook.Default;
        _iconName = iconName;
        LoadImage();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SidebarRow Row => _row;

    public Guid Id => _row.Id;

    public SidebarRowKind Kind => _row.Kind;

    public bool IsWorkspace => _row.Kind == SidebarRowKind.Workspace;

    public bool IsSession => _row.Kind == SidebarRowKind.Session;

    public string Label => _row.Label;

    public double FontSize =>
        AppSettings.ClampSidebarFontSize(_settings.SidebarFontSize ?? AppSettings.DefaultSidebarFontSize);

    public double RowHeight => AppSettings.SidebarRowHeight(FontSize);

    public Visibility WorkspaceVisibility => IsWorkspace ? Visibility.Visible : Visibility.Collapsed;

    public string WorkspaceGlyph => SidebarWorkspaceIcon.Glyph(_iconName, SymbolGlyphs.Has) ?? "";

    public ImageSource? WorkspaceImage => _image;

    public Visibility WorkspaceGlyphVisibility => IconVisibility(SidebarWorkspaceIconKind.Glyph);

    public Visibility WorkspaceImageVisibility => IconVisibility(SidebarWorkspaceIconKind.Image);

    public Visibility GridIconVisibility => IconVisibility(SidebarWorkspaceIconKind.Grid);

    private Visibility IconVisibility(SidebarWorkspaceIconKind kind) =>
        IsWorkspace && SidebarWorkspaceIcon.KindOf(_iconName, SymbolGlyphs.Has, _imageFailed) == kind
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void LoadImage()
    {
        _imageFailed = false;
        _image = null;
        if (!IsWorkspace || SidebarWorkspaceIcon.ImageFile(_iconName) is not { } path) return;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            _imageFailed = true;
            return;
        }

        // the decode size is set before the source, so the file is decoded once at row size
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = new SvgImageSource { RasterizePixelWidth = 16, RasterizePixelHeight = 16 };
            svg.OpenFailed += (sender, _) => ImageFailed(sender);
            svg.UriSource = uri;
            _image = svg;
        }
        else
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 16, DecodePixelType = DecodePixelType.Logical };
            bitmap.ImageFailed += (sender, _) => ImageFailed(sender);
            bitmap.UriSource = uri;
            _image = bitmap;
        }
    }

    // a failure of an image the row no longer shows is ignored
    private void ImageFailed(object sender)
    {
        if (!ReferenceEquals(sender, _image)) return;
        _imageFailed = true;
        Raise(nameof(WorkspaceImageVisibility), nameof(GridIconVisibility));
    }

    public Visibility SessionVisibility => IsSession ? Visibility.Visible : Visibility.Collapsed;

    public string AutomationName => SidebarAutomationText.Row(_row, _surfaceStatus);

    public string ChevronAutomationName => SidebarAutomationText.Chevron(_row);

    public string SurfaceAutomationName => SidebarAutomationText.Surface(_surfaceStatus);

    public string AgentAutomationName => SidebarAutomationText.Agent(_row.Indicator.Status);

    public string UnseenAutomationName => SidebarAutomationText.Unseen(_row.Unseen);

    public string FocusedWorkspaceAutomationName => "Focused workspace";

    public string Chevron => _row.Collapsed ? "" : ""; // ChevronRight / ChevronDown

    // a mode change rebuilds the rows, so the mode is fixed for a row's lifetime
    public Thickness ContentMargin => new(8 + SidebarRowIndent.Of(Kind, _mode), 0, 8, 0);

    public FontWeight LabelWeight =>
        IsWorkspace ? Microsoft.UI.Text.FontWeights.SemiBold
        : IsListSelected ? Microsoft.UI.Text.FontWeights.Bold
        : Microsoft.UI.Text.FontWeights.Normal;

    /// <summary>Whether the list shows this row as selected. The view sets it from the list's own
    /// selection, which also covers extended selection.</summary>
    public bool IsListSelected
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            Raise(nameof(IsListSelected), nameof(RowFrame), nameof(LabelWeight));
        }
    }

    // a colored row shows selection without changing color, so it gets a frame in the theme foreground
    private bool IsColoredSelection => _look.Row is not null && IsListSelected;

    public Brush? RowBackground => _look.Row is { } colors ? BrushFrom(colors.Background) : null;

    public Brush RowFrame => IsColoredSelection
        ? BrushFrom(_look.Row!.Foreground)
        : new SolidColorBrush(Colors.Transparent);

    public Brush? RowForeground => _look.Row is { } colors ? BrushFrom(colors.Foreground) : null;

    public Brush? LabelForeground => (_look.Row ?? _look.Label) is { } colors ? BrushFrom(colors.Foreground) : null;

    public Brush? LabelBlockBackground => _look.Label is { } colors ? BrushFrom(colors.Background) : null;

    public Thickness LabelBlockPadding => _look.Label is null ? new Thickness(0) : new Thickness(4, 1, 4, 1);

    /// <summary>Whether the pointer is over this row. The view sets it from the row's pointer events
    /// and clears it when the row's container is recycled.</summary>
    public bool IsPointerOver
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            Raise(nameof(IsPointerOver), nameof(AddSessionVisibility));
        }
    }

    // the workspace row's inline "+" (upstream workspace-add-session)
    public Visibility AddSessionVisibility =>
        SidebarAddSessionButton.IsShown(Kind, IsPointerOver, _settings) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FocusPillVisibility =>
        IsWorkspace && _row.Focused ? Visibility.Visible : Visibility.Collapsed;

    // session base icon: a terminal rect, split adds the divider, flagged fills it (upstream's
    // terminal / rectangle.split.2x1 (+.fill) swap, drawn as Path so no icon-font gamble)
    public string IconData => _row.HasSplit
        ? "M0.5,2.5 H15.5 V12.5 H0.5 Z M8,2.5 V12.5"
        : "M0.5,2.5 H15.5 V12.5 H0.5 Z";

    public Brush IconFill => SidebarSessionIcon.IsFilled(_row.Flagged, _mode)
        ? _look.Row is { } colors
            ? BrushFrom(colors.Foreground, alpha: 0x60)
            : new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))
        : new SolidColorBrush(Colors.Transparent);

    // agent-status glyph (idle → hidden; shape/color resolved per-call → settings → defaults)
    public Visibility GlyphVisibility =>
        IsSession && _row.Indicator.Status != AgentStatus.Idle ? Visibility.Visible : Visibility.Collapsed;

    public bool GlyphBlink => _row.Indicator.Blink && _row.Indicator.Status != AgentStatus.Idle;

    public StatusShape GlyphShape =>
        _row.Indicator.Shape ?? _settings.EffectiveStatusShape(_row.Indicator.Status) ?? StatusShape.Circle;

    public Brush GlyphFill =>
        BrushFrom(_settings.EffectiveStatusColorHex(_row.Indicator.Status, _row.Indicator.Color));

    public string GlyphData => GlyphShape switch
    {
        StatusShape.Square => "M1.5,1.5 H11.5 V11.5 H1.5 Z",
        StatusShape.Triangle => "M6.5,1 L12.3,12 L0.7,12 Z",
        StatusShape.Diamond => "M6.5,0.5 L12.5,6.5 L6.5,12.5 L0.5,6.5 Z",
        StatusShape.Capsule => "M4,2.5 H9 A4,4 0 0 1 9,10.5 H4 A4,4 0 0 1 4,2.5 Z",
        StatusShape.Star => "M6.5,0.6 L8.1,4.8 L12.6,5.0 L9.1,7.9 L10.3,12.2 L6.5,9.8 L2.7,12.2 " +
                            "L3.9,7.9 L0.4,5.0 L4.9,4.8 Z",
        _ => "M6.5,0.75 A5.75,5.75 0 1 1 6.49,0.75 Z", // circle
    };

    // unseen badge (gated render-only by notificationBadgeEnabled; 99+ cap)
    public Visibility BadgeVisibility =>
        SidebarBadge.IsShown(_row.Unseen, _settings)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string BadgeText => _row.Unseen > 99 ? "99+" : _row.Unseen.ToString();

    public bool IsSurfacePending => IsSession && _surfaceStatus == SidebarSurfaceStatus.Pending;

    public bool IsSurfaceFailed => IsSession && _surfaceStatus == SidebarSurfaceStatus.Failed;

    public Visibility PendingVisibility => IsSurfacePending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FailedVisibility => IsSurfaceFailed ? Visibility.Visible : Visibility.Collapsed;

    // inline rename
    public bool IsEditing
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            Raise(nameof(IsEditing), nameof(EditorVisibility), nameof(LabelVisibility));
        }
    }

    public string EditText
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            Raise(nameof(EditText));
        }
    } = "";

    public Visibility EditorVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LabelVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Re-apply a fresh row/settings pair; notifies only when the content differs (rows
    /// are records, so equality is the whole diff). Never disturbs an in-progress rename.</summary>
    public void Update(
        SidebarRow row,
        AppSettings settings,
        SidebarSurfaceStatus surfaceStatus = SidebarSurfaceStatus.Ready,
        SidebarRowLook? look = null,
        string? iconName = null)
    {
        look ??= SidebarRowLook.Default;
        var settingsChanged = !ReferenceEquals(_settings, settings);
        var surfaceStatusChanged = _surfaceStatus != surfaceStatus;
        if (_row == row && !settingsChanged && !surfaceStatusChanged && _look == look && _iconName == iconName) return;
        _row = row;
        _settings = settings;
        _surfaceStatus = surfaceStatus;
        _look = look;
        if (_iconName != iconName)
        {
            _iconName = iconName;
            LoadImage();
        }
        Raise(nameof(Label), nameof(Chevron), nameof(LabelWeight),
              nameof(WorkspaceGlyph), nameof(WorkspaceGlyphVisibility), nameof(GridIconVisibility),
              nameof(WorkspaceImage), nameof(WorkspaceImageVisibility),
              nameof(FontSize), nameof(RowHeight),
              nameof(FocusPillVisibility), nameof(AddSessionVisibility), nameof(IconData), nameof(IconFill),
              nameof(GlyphVisibility), nameof(GlyphBlink), nameof(GlyphShape), nameof(GlyphFill),
              nameof(GlyphData), nameof(BadgeVisibility), nameof(BadgeText),
              nameof(AutomationName), nameof(ChevronAutomationName),
              nameof(SurfaceAutomationName), nameof(AgentAutomationName),
              nameof(UnseenAutomationName), nameof(FocusedWorkspaceAutomationName),
              nameof(IsSurfacePending), nameof(IsSurfaceFailed),
              nameof(PendingVisibility), nameof(FailedVisibility),
              nameof(RowBackground), nameof(RowFrame), nameof(RowForeground),
              nameof(LabelForeground), nameof(LabelBlockBackground), nameof(LabelBlockPadding));
    }

    /// <summary>A brush from a "#rrggbb" color, as settings and theme files write them.</summary>
    private static SolidColorBrush BrushFrom(string hex, byte alpha = 0xFF) =>
        new(Color.FromArgb(alpha,
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16)));

    private void Raise(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
