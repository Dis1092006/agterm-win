using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgTerm.App;

/// <summary>
/// The panel of the page in the selected session's overlay slot (upstream HtmlOverlayView): the identity
/// strip, the page and the failure view. It holds a web view control for every live page, so a page
/// survives session switches, and shows the one it is given. It only draws; the window wires its
/// buttons to the store and the page registry.
/// </summary>
public sealed partial class HtmlOverlayView : UserControl
{
    private readonly Dictionary<Guid, WebView2> _controls = [];
    private readonly StripButton _back, _forward, _reload, _browser, _explorer, _hide, _close;
    private int? _sizePercent;
    private OverlayPointSize _points;
    private double _chrome;

    public HtmlOverlayView()
    {
        InitializeComponent();
        _back = new StripButton("\uE72B", "Back", "htmlOverlay.back", () => Navigate(HtmlNavigation.Back));
        _forward = new StripButton("\uE72A", "Forward", "htmlOverlay.forward", () => Navigate(HtmlNavigation.Forward));
        _reload = new StripButton("\uE72C", "Reload", "htmlOverlay.reload", () => Raise(ReloadRequested));
        _browser = new StripButton("\uE774", "Open in Browser", "htmlOverlay.browser", () => Navigate(HtmlNavigation.Browser));
        _explorer = new StripButton("\uE838", "Show in Explorer", "htmlOverlay.finder", () => Navigate(HtmlNavigation.Finder));
        _hide = new StripButton("\uE738", "Hide", "htmlOverlay.hide", () => Raise(HideRequested));
        _hide.Element.Visibility = Visibility.Collapsed;
        _close = new StripButton("\uE711", "Close", "htmlOverlay.close", () => Raise(CloseRequested));
        foreach (var button in new[] { _back, _forward, _reload }) LeadingButtons.Children.Add(button.Element);
        foreach (var button in new[] { _browser, _explorer, _hide, _close }) TrailingButtons.Children.Add(button.Element);
        // the buttons handle their own presses, so this sees only the strip's empty area and its label
        Strip.PointerPressed += (_, _) => StripPressed?.Invoke();
        SizeChanged += (_, _) => PlacePanel();
    }

    /// <summary>A strip button asked for a history or hand-off action on the shown page.</summary>
    public event Action<Guid, HtmlNavigation>? NavigateRequested;

    public event Action<Guid>? ReloadRequested;

    public event Action<Guid>? HideRequested;

    public event Action<Guid>? CloseRequested;

    /// <summary>A press on the strip outside its buttons.</summary>
    public event Action? StripPressed;

    /// <summary>Whether the strip offers Hide. It follows the title bar's overlay button, which is the
    /// way back to a hidden page.</summary>
    public bool HideShown
    {
        get => _hide.Element.Visibility == Visibility.Visible;
        set => _hide.Element.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The page on screen, or null while the panel is hidden.</summary>
    public Guid? ShownPage { get; private set; }

    /// <summary>A new control for page id, in the panel and in the tree, for the page view to take over.</summary>
    public WebView2 AddControl(Guid id, HtmlOverlayTheme theme)
    {
        var control = new WebView2
        {
            DefaultBackgroundColor = Parse(theme.Background),
            Visibility = id == ShownPage ? Visibility.Visible : Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(control, "htmlOverlay.page");
        _controls[id] = control;
        PageHost.Children.Add(control);
        return control;
    }

    public void RemoveControl(Guid id)
    {
        if (_controls.Remove(id, out var control)) PageHost.Children.Remove(control);
    }

    /// <summary>Whether focused is one of the panel's page controls.</summary>
    public bool HoldsFocus(object? focused) => focused is WebView2 control && _controls.ContainsValue(control);

    /// <summary>Shows page id in a panel sized as the overlay slot asks.</summary>
    public void Present(Guid id, HtmlOverlayStrip strip, HtmlOverlayTheme theme, int? sizePercent,
                        OverlayPointSize points, double chrome)
    {
        ShownPage = id;
        (_sizePercent, _points, _chrome) = (sizePercent, points, chrome);
        foreach (var (key, control) in _controls)
            control.Visibility = key == id ? Visibility.Visible : Visibility.Collapsed;
        ApplyTheme(theme);
        Update(strip);
        Visibility = Visibility.Visible;
        PlacePanel();
    }

    public void Conceal()
    {
        ShownPage = null;
        Visibility = Visibility.Collapsed;
        foreach (var control in _controls.Values) control.Visibility = Visibility.Collapsed;
    }

    /// <summary>Redraws the strip and the failure view from the shown page's latest report.</summary>
    public void Update(HtmlOverlayStrip strip)
    {
        var chrome = strip.Shown ? Visibility.Visible : Visibility.Collapsed;
        Strip.Visibility = chrome;
        Rule.Visibility = chrome;
        StripRow.Height = new GridLength(strip.Shown ? HtmlOverlay.StripHeight : 0);
        RuleRow.Height = new GridLength(strip.Shown ? 1 : 0);
        var navigation = strip.Navigation ? Visibility.Visible : Visibility.Collapsed;
        LeadingButtons.Visibility = navigation;
        _browser.Element.Visibility = navigation;
        _explorer.Element.Visibility = navigation;
        _back.Enabled = strip.CanGoBack;
        _forward.Enabled = strip.CanGoForward;
        IdentityText.Text = strip.Identity;
        ToolTipService.SetToolTip(IdentityText, strip.IdentityTip);
        var titled = strip.Title is not null ? Visibility.Visible : Visibility.Collapsed;
        TitleDot.Visibility = titled;
        TitleText.Visibility = titled;
        TitleText.Text = strip.Title ?? "";
        ToolTipService.SetToolTip(TitleText, strip.Title);
        FailureView.Visibility = strip.Failed ? Visibility.Visible : Visibility.Collapsed;
        FailureError.Text = strip.Error ?? "";
        FailureError.Visibility = strip.Error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyTheme(HtmlOverlayTheme theme)
    {
        var background = Parse(theme.Background);
        var foreground = Parse(theme.Foreground);
        PagePanel.Background = new SolidColorBrush(background);
        Strip.Background = new SolidColorBrush(Blend(background, foreground, 0.06));
        Rule.Background = new SolidColorBrush(Faded(foreground, 0.1));
        var text = new SolidColorBrush(foreground);
        IdentityText.Foreground = text;
        TitleDot.Foreground = new SolidColorBrush(Faded(foreground, 0.4));
        TitleText.Foreground = new SolidColorBrush(Faded(foreground, 0.55));
        FailureView.Background = new SolidColorBrush(background);
        FailureIcon.Foreground = text;
        FailureTitle.Foreground = text;
        FailureError.Foreground = text;
        foreach (var button in new[] { _back, _forward, _reload, _browser, _explorer, _hide, _close }) button.Tint = foreground;
        if (ShownPage is { } id && _controls.TryGetValue(id, out var control)) control.DefaultBackgroundColor = background;
    }

    private void PlacePanel()
    {
        if (ShownPage is null) return;
        var (x, y, width, height) = HtmlOverlayStrip.PanelFrame((ActualWidth, ActualHeight), _sizePercent, _points, _chrome);
        Canvas.SetLeft(PagePanel, x);
        Canvas.SetTop(PagePanel, y);
        PagePanel.Width = Math.Max(0, width);
        PagePanel.Height = Math.Max(0, height);
        var floating = _sizePercent is not null;
        PagePanel.CornerRadius = new CornerRadius(floating ? 10 : 0);
        PagePanel.BorderThickness = new Thickness(floating ? 1 : 0);
        PagePanel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    }

    private void Navigate(HtmlNavigation navigation)
    {
        if (ShownPage is { } id) NavigateRequested?.Invoke(id, navigation);
    }

    private void Raise(Action<Guid>? handler)
    {
        if (ShownPage is { } id) handler?.Invoke(id);
    }

    // theme colors are #rrggbb: HtmlOverlayTheme replaces anything else
    private static Color Parse(string hex) => Color.FromArgb(0xFF,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));

    private static Color Faded(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B);

    private static Color Blend(Color under, Color over, double opacity)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * opacity);
        return Color.FromArgb(0xFF, Mix(under.R, over.R), Mix(under.G, over.G), Mix(under.B, over.B));
    }

    /// <summary>A strip button (upstream HtmlToolbarButtonStyle): the theme's text color, dimmed when
    /// disabled or pressed, with a fill on hover and press. It never takes the keyboard focus.</summary>
    private sealed class StripButton
    {
        private readonly FontIcon _icon;
        private readonly SolidColorBrush _fill = new(Colors.Transparent);
        private bool _hovered, _pressed, _enabled = true;
        private Color _tint = Colors.White;

        public StripButton(string glyph, string label, string automationId, Action invoke)
        {
            _icon = new FontIcon { Glyph = glyph, FontSize = 12 };
            Element = new Border
            {
                Width = 22,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                Background = _fill,
                Child = _icon,
            };
            AutomationProperties.SetName(Element, label);
            AutomationProperties.SetAutomationId(Element, automationId);
            ToolTipService.SetToolTip(Element, label);
            Element.PointerEntered += (_, _) => { _hovered = true; Paint(); };
            Element.PointerExited += (_, _) => { _hovered = false; _pressed = false; Paint(); };
            Element.PointerCaptureLost += (_, _) => { _pressed = false; Paint(); };
            Element.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (!_enabled) return;
                _pressed = Element.CapturePointer(e.Pointer);
                Paint();
            };
            Element.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                var fire = _pressed && _hovered && _enabled;
                _pressed = false;
                Element.ReleasePointerCapture(e.Pointer);
                Paint();
                if (fire) invoke();
            };
            Paint();
        }

        public Border Element { get; }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; Paint(); }
        }

        public Color Tint
        {
            set { _tint = value; Paint(); }
        }

        private void Paint()
        {
            _icon.Foreground = new SolidColorBrush(Faded(_tint, _enabled ? (_pressed ? 0.55 : 1) : 0.3));
            _fill.Color = Faded(_tint, _pressed ? 0.22 : _hovered && _enabled ? 0.1 : 0);
        }
    }
}
