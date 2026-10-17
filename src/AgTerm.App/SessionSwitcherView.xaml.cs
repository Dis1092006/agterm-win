using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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

    public void Show(IReadOnlyList<TitleBarRecentSession> candidates, int index)
    {
        Rows.Children.Clear();
        for (var i = 0; i < candidates.Count; i++)
            Rows.Children.Add(Row(candidates[i], selected: i == index));
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        Rows.Children.Clear();
    }

    private Border Row(TitleBarRecentSession session, bool selected)
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
        var row = new Border
        {
            Style = (Style)Resources[selected ? "SwitcherSelectedRowStyle" : "SwitcherRowStyle"],
            Child = text,
        };
        AutomationProperties.SetName(row, session.Title);
        if (selected)
            AutomationProperties.SetItemStatus(row, "Selected");
        return row;
    }
}
