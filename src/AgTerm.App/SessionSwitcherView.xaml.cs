using AgTerm.Xterm;
using AgTerm.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AgTerm.App;

/// <summary>The Ctrl-Tab switcher's overlay: the candidates as name over "workspace · detail" rows, the
/// highlighted one marked. It only draws; SessionSwitcher holds the state.</summary>
public sealed partial class SessionSwitcherView : UserControl
{
    public SessionSwitcherView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => SwitcherPanel.Margin = new Thickness(24, e.NewSize.Height * 0.12, 24, 24);
    }

    public void SetPanelBackground(Brush? background)
    {
        if (background is null)
            SwitcherPanel.ClearValue(Border.BackgroundProperty);
        else
            SwitcherPanel.Background = background;
    }

    public void Show(IReadOnlyList<TitleBarRecentSession> candidates, int index, AppSettings settings)
    {
        Rows.Children.Clear();
        for (var i = 0; i < candidates.Count; i++)
            Rows.Children.Add(Row(candidates[i], selected: i == index, settings));
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        Rows.Children.Clear();
    }

    private Border Row(TitleBarRecentSession session, bool selected, AppSettings settings)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = session.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = session.Subtitle,
            FontSize = 12,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var indicator = session.Indicator;
        var presentation = new PaletteItemViewModel(session.Id.ToString("D"), session.Title, session.Subtitle,
            status: indicator.Status, statusColor: indicator.Color, statusShape: indicator.Shape);
        presentation.RefreshIndicator(indicator, settings);
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(text);
        var glyph = new SessionStatusGlyph
        {
            ShapeData = presentation.GlyphData,
            ColorHex = presentation.StatusColor,
            Shown = presentation.HasStatusGlyph,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(glyph, 1);
        content.Children.Add(glyph);
        var row = new Border
        {
            Style = (Style)Resources[selected ? "SwitcherSelectedRowStyle" : "SwitcherRowStyle"],
            Child = content,
        };
        AutomationProperties.SetName(row, presentation.AutomationName);
        if (selected)
            AutomationProperties.SetItemStatus(row, "Selected");
        return row;
    }
}
