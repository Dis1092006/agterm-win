using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
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
    private string? _imageInk;
    private int _imageLoad;

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

    public double RowHeight => ShowsInfo ? SidebarInfoMetrics.RowHeight(FontSize) : AppSettings.SidebarRowHeight(FontSize);

    /// <summary>The band the title row centers in: the plain row height, with or without an info line.</summary>
    public double TitleBandHeight => AppSettings.SidebarRowHeight(FontSize);

    // the info line: the setting gates drawing only, so a hidden line stays in the store
    private bool ShowsInfo =>
        IsSession && _row.Info is not null && SessionInfoDisplays.Shows(_settings.EffectiveSessionInfoDisplay, _mode);

    public Visibility InfoVisibility => ShowsInfo ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<SidebarInfoRun> InfoRuns => ShowsInfo && _row.Info is { } info
        ? SidebarInfoLine.Runs(info.Spans, _look.Info, IsListSelected)
        : [];

    public double InfoFontSize => SidebarInfoMetrics.FontSize(FontSize);

    public double InfoLineHeight => SidebarInfoMetrics.LineHeight(FontSize);

    // the line starts where the name does, past the icon column and its spacing, and moves left with
    // the name when the icon is hidden
    public Thickness InfoMargin => new(ContentMargin.Left + NameOffset, SidebarInfoMetrics.Top(FontSize), 8, 0);

    private const double NameOffset = 22;

    public Visibility WorkspaceVisibility => IsWorkspace ? Visibility.Visible : Visibility.Collapsed;

    public string WorkspaceGlyph => SidebarWorkspaceIcon.Glyph(_iconName, SymbolGlyphs.Has) ?? "";

    public ImageSource? WorkspaceImage => _image;

    public Visibility WorkspaceGlyphVisibility => IconVisibility(SidebarWorkspaceIconKind.Glyph);

    public Visibility WorkspaceImageVisibility => IconVisibility(SidebarWorkspaceIconKind.Image);

    public Visibility GridIconVisibility => IconVisibility(SidebarWorkspaceIconKind.Grid);

    private Visibility IconVisibility(SidebarWorkspaceIconKind kind) =>
        IsWorkspace && SidebarRowIcon.IsShown(Kind, _settings)
        && SidebarWorkspaceIcon.KindOf(_iconName, SymbolGlyphs.Has, _imageFailed) == kind
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void LoadImage()
    {
        var load = ++_imageLoad;
        _imageFailed = false;
        _image = null;
        _imageInk = _look.Tint.Icon;
        if (!IsWorkspace || SidebarWorkspaceIcon.ImageFile(_iconName) is not { } path) return;
        // the wire takes absolute file paths only, but a hand-edited snapshot can hold any URL
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            _imageFailed = true;
            return;
        }

        var isSvg = Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);
        if (_imageInk is { } ink)
        {
            _ = LoadSilhouetteAsync(load, uri.LocalPath, isSvg, ink);
            return;
        }

        // a bitmap's decode size is set before its source, so the file is decoded once at row size;
        // an SVG renders at the Image's size
        if (isSvg)
        {
            var svg = new SvgImageSource();
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

    /// <summary>An icon tint draws an image file as a one-color silhouette (upstream draws it as a
    /// template image). The image shows when it is ready; a load the row replaced meanwhile is dropped.</summary>
    private async Task LoadSilhouetteAsync(int load, string path, bool isSvg, string ink)
    {
        ImageSource? image;
        try
        {
            image = isSvg ? await SvgSilhouetteAsync(path, ink) : await BitmapSilhouetteAsync(path, ink);
        }
        catch (Exception) // a missing, unreadable or undecodable file shows the grid, as for an untinted image
        {
            image = null;
        }

        if (load != _imageLoad) return;
        _image = image;
        _imageFailed = image is null;
        Raise(nameof(WorkspaceImage), nameof(WorkspaceImageVisibility), nameof(GridIconVisibility));
    }

    private static async Task<ImageSource?> SvgSilhouetteAsync(string path, string ink)
    {
        if (SvgSilhouette.Recolor(await File.ReadAllTextAsync(path), ink) is not { } svg) return null;
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(svg).AsBuffer());
        stream.Seek(0);
        var source = new SvgImageSource();
        return await source.SetSourceAsync(stream) == SvgImageSourceLoadStatus.Success ? source : null;
    }

    // decoded at up to 64 pixels a side, so the 16-pixel icon stays sharp at 400% scale
    private static async Task<ImageSource?> BitmapSilhouetteAsync(string path, string ink)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var scale = Math.Min(1.0, 64.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
        var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();
        if (!BitmapSilhouette.Recolor(pixels, ink)) return null;
        var bitmap = new WriteableBitmap((int)width, (int)height);
        using (var target = bitmap.PixelBuffer.AsStream())
            await target.WriteAsync(pixels);
        bitmap.Invalidate();
        return bitmap;
    }

    // a failure of an image the row no longer shows is ignored
    private void ImageFailed(object sender)
    {
        if (!ReferenceEquals(sender, _image)) return;
        _imageFailed = true;
        Raise(nameof(WorkspaceImageVisibility), nameof(GridIconVisibility));
    }

    public Visibility SessionIconVisibility =>
        IsSession && SidebarRowIcon.IsShown(Kind, _settings) ? Visibility.Visible : Visibility.Collapsed;

    public string AutomationName => SidebarAutomationText.Row(_row, _surfaceStatus);

    public string ChevronAutomationName => SidebarAutomationText.Chevron(_row);

    public string SurfaceAutomationName => SidebarAutomationText.Surface(_surfaceStatus);

    public string AgentAutomationName => SidebarAutomationText.Agent(_row.Indicator.Status);

    public string UnseenAutomationName => SidebarAutomationText.Unseen(_row.Unseen);

    public string FocusedWorkspaceAutomationName => "Focused workspace";

    public string Chevron => _row.Collapsed ? "" : ""; // ChevronRight / ChevronDown

    // a mode change rebuilds the rows, so the mode is fixed for a row's lifetime
    public Thickness ContentMargin =>
        new(8 + SidebarRowIndent.Of(Kind, _mode, SidebarRowIcon.IsShown(Kind, _settings)), 0, 8, 0);

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
            Raise(nameof(IsListSelected), nameof(RowFrame), nameof(LabelWeight), nameof(InfoRuns));
        }
    }

    // a colored row shows selection without changing color, so it gets a frame in the theme foreground
    private bool IsColoredSelection => _look.Row is not null && IsListSelected;

    public Brush? RowBackground => _look.Row is { } colors ? BrushFrom(colors.Background) : null;

    public Brush RowFrame => IsColoredSelection
        ? BrushFrom(_look.Row!.Foreground)
        : new SolidColorBrush(Colors.Transparent);

    public Brush? RowForeground => _look.Row is { } colors ? BrushFrom(colors.Foreground) : null;

    /// <summary>The icon's color: the icon tint, else the row foreground.</summary>
    public Brush? IconForeground => _look.Tint.Icon is { } ink ? BrushFrom(ink) : RowForeground;

    public Brush? LabelForeground => _look.Tint.Name is { } ink ? BrushFrom(ink)
        : (_look.Row ?? _look.Label) is { } colors ? BrushFrom(colors.Foreground)
        : null;

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

    /// <summary>The fill of a flagged default row's icon. The sidebar shares one brush in the theme
    /// foreground across its rows and recolors it with the theme.</summary>
    public Brush DefaultIconFill { get; init; } = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

    public Brush IconFill => SidebarSessionIcon.IsFilled(_row.Flagged, _mode)
        ? MarkFill()
        : new SolidColorBrush(Colors.Transparent);

    /// <summary>A workspace in the focus set draws the filled grid (upstream square.grid.2x2.fill),
    /// keyed on membership alone so the marked set stays legible with the filter off.</summary>
    public Brush GridIconFill => IsWorkspace && _row.Focused
        ? MarkFill()
        : new SolidColorBrush(Colors.Transparent);

    // a tinted icon fills in its tint, as upstream fills the tinted symbol
    private Brush MarkFill() => _look.Tint.Icon is { } ink ? BrushFrom(ink, alpha: 0x60)
        : _look.Row is { } colors ? BrushFrom(colors.Foreground, alpha: 0x60)
        : DefaultIconFill;

    // agent-status glyph (idle → hidden; shape/color resolved per-call → settings → defaults)
    public Visibility GlyphVisibility =>
        IsSession && _row.Indicator.Status != AgentStatus.Idle ? Visibility.Visible : Visibility.Collapsed;

    public bool GlyphBlink => _row.Indicator.Blink && _row.Indicator.Status != AgentStatus.Idle;

    public StatusShape GlyphShape =>
        StatusGlyphPresentation.Shape(_row.Indicator, _settings);

    public Brush GlyphFill =>
        BrushFrom(StatusGlyphPresentation.Color(_row.Indicator, _settings));

    public string GlyphData => StatusGlyphPresentation.Data(GlyphShape);

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
        if (_iconName != iconName || _imageInk != look.Tint.Icon)
        {
            _iconName = iconName;
            LoadImage();
        }
        Raise(nameof(Label), nameof(Chevron), nameof(LabelWeight),
              nameof(WorkspaceGlyph), nameof(WorkspaceGlyphVisibility), nameof(GridIconVisibility),
              nameof(WorkspaceImage), nameof(WorkspaceImageVisibility), nameof(SessionIconVisibility), nameof(ContentMargin),
              nameof(FontSize), nameof(RowHeight), nameof(TitleBandHeight),
              nameof(InfoVisibility), nameof(InfoRuns), nameof(InfoMargin), nameof(InfoFontSize), nameof(InfoLineHeight),
              nameof(FocusPillVisibility), nameof(AddSessionVisibility), nameof(IconData), nameof(IconFill), nameof(GridIconFill),
              nameof(GlyphVisibility), nameof(GlyphBlink), nameof(GlyphShape), nameof(GlyphFill),
              nameof(GlyphData), nameof(BadgeVisibility), nameof(BadgeText),
              nameof(AutomationName), nameof(ChevronAutomationName),
              nameof(SurfaceAutomationName), nameof(AgentAutomationName),
              nameof(UnseenAutomationName), nameof(FocusedWorkspaceAutomationName),
              nameof(IsSurfacePending), nameof(IsSurfaceFailed),
              nameof(PendingVisibility), nameof(FailedVisibility),
              nameof(RowBackground), nameof(RowFrame), nameof(RowForeground), nameof(IconForeground),
              nameof(LabelForeground), nameof(LabelBlockBackground), nameof(LabelBlockPadding));
    }

    /// <summary>A brush from a "#rrggbb" color, as settings and theme files write them.</summary>
    internal static SolidColorBrush BrushFrom(string hex, byte alpha = 0xFF) =>
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
