using System.ComponentModel;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
    private SidebarSurfaceStatus _surfaceStatus;
    private bool _isEditing;
    private string _editText = "";

    public SidebarRowViewModel(
        SidebarRow row,
        AppSettings settings,
        SidebarSurfaceStatus surfaceStatus = SidebarSurfaceStatus.Ready)
    {
        _row = row;
        _settings = settings;
        _surfaceStatus = surfaceStatus;
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

    public Visibility SessionVisibility => IsSession ? Visibility.Visible : Visibility.Collapsed;

    public string AutomationName => SidebarAutomationText.Row(_row, _surfaceStatus);

    public string ChevronAutomationName => SidebarAutomationText.Chevron(_row);

    public string SurfaceAutomationName => SidebarAutomationText.Surface(_surfaceStatus);

    public string AgentAutomationName => SidebarAutomationText.Agent(_row.Indicator.Status);

    public string UnseenAutomationName => SidebarAutomationText.Unseen(_row.Unseen);

    public string FocusedWorkspaceAutomationName => "Focused workspace";

    public string Chevron => _row.Collapsed ? "" : ""; // ChevronRight / ChevronDown

    public double LabelOpacity => IsWorkspace && !_row.ActiveWorkspace ? 0.75 : 1.0;

    public FontWeight LabelWeight => IsWorkspace
        ? Microsoft.UI.Text.FontWeights.SemiBold
        : Microsoft.UI.Text.FontWeights.Normal;

    public Visibility FocusPillVisibility =>
        IsWorkspace && _row.Focused ? Visibility.Visible : Visibility.Collapsed;

    // session base icon: a terminal rect, split adds the divider, flagged fills it (upstream's
    // terminal / rectangle.split.2x1 (+.fill) swap, drawn as Path so no icon-font gamble)
    public string IconData => _row.HasSplit
        ? "M0.5,2.5 H15.5 V12.5 H0.5 Z M8,2.5 V12.5"
        : "M0.5,2.5 H15.5 V12.5 H0.5 Z";

    public Brush IconFill => _row.Flagged
        ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))
        : new SolidColorBrush(Colors.Transparent);

    // agent-status glyph (idle → hidden; shape/color resolved per-call → settings → defaults)
    public Visibility GlyphVisibility =>
        IsSession && _row.Indicator.Status != AgentStatus.Idle ? Visibility.Visible : Visibility.Collapsed;

    public bool GlyphBlink => _row.Indicator.Blink && _row.Indicator.Status != AgentStatus.Idle;

    public StatusShape GlyphShape =>
        _row.Indicator.Shape ?? _settings.EffectiveStatusShape(_row.Indicator.Status) ?? StatusShape.Circle;

    public Brush GlyphFill
    {
        get
        {
            var hex = _settings.EffectiveStatusColorHex(_row.Indicator.Status, _row.Indicator.Color);
            return new SolidColorBrush(Color.FromArgb(0xFF,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16)));
        }
    }

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
        _settings.NotificationBadgeEnabled != false && _row.Unseen > 0
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
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            Raise(nameof(IsEditing), nameof(EditorVisibility), nameof(LabelVisibility));
        }
    }

    public string EditText
    {
        get => _editText;
        set
        {
            if (_editText == value) return;
            _editText = value;
            Raise(nameof(EditText));
        }
    }

    public Visibility EditorVisibility => _isEditing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LabelVisibility => _isEditing ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Re-apply a fresh row/settings pair; notifies only when the content differs (rows
    /// are records, so equality is the whole diff). Never disturbs an in-progress rename.</summary>
    public void Update(
        SidebarRow row,
        AppSettings settings,
        SidebarSurfaceStatus surfaceStatus = SidebarSurfaceStatus.Ready)
    {
        var settingsChanged = !ReferenceEquals(_settings, settings);
        var surfaceStatusChanged = _surfaceStatus != surfaceStatus;
        if (_row == row && !settingsChanged && !surfaceStatusChanged) return;
        _row = row;
        _settings = settings;
        _surfaceStatus = surfaceStatus;
        Raise(nameof(Label), nameof(Chevron), nameof(LabelOpacity), nameof(LabelWeight),
              nameof(FontSize), nameof(RowHeight),
              nameof(FocusPillVisibility), nameof(IconData), nameof(IconFill),
              nameof(GlyphVisibility), nameof(GlyphBlink), nameof(GlyphShape), nameof(GlyphFill),
              nameof(GlyphData), nameof(BadgeVisibility), nameof(BadgeText),
              nameof(AutomationName), nameof(ChevronAutomationName),
              nameof(SurfaceAutomationName), nameof(AgentAutomationName),
              nameof(UnseenAutomationName), nameof(FocusedWorkspaceAutomationName),
              nameof(IsSurfacePending), nameof(IsSurfaceFailed),
              nameof(PendingVisibility), nameof(FailedVisibility));
    }

    private void Raise(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
