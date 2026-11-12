using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AgTerm.App;

/// <summary>
/// The page strip's label: the identity, the dot and the title, centered as one line. The title gives
/// up width first, as HtmlOverlayStrip.LabelWidths decides, so a long label is cut inside its texts and
/// never at its own edges.
/// </summary>
public sealed partial class HtmlStripLabelPanel : Panel
{
    private const double TitleMinimum = 16;
    private double _identity, _title, _total;

    public double Spacing { get; set; } = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3) return default;
        var (identity, dot, title) = (Children[0], Children[1], Children[2]);
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children) child.Measure(unbounded);

        var titled = title.Visibility == Visibility.Visible;
        var separator = dot.DesiredSize.Width + 2 * Spacing;
        (_identity, _title) = HtmlOverlayStrip.LabelWidths(availableSize.Width, identity.DesiredSize.Width,
            titled ? title.DesiredSize.Width : null, separator, TitleMinimum);
        identity.Measure(new Size(_identity, availableSize.Height));
        title.Measure(new Size(_title, availableSize.Height));
        _total = _identity + (_title > 0 ? separator + _title : 0);
        var height = Math.Max(identity.DesiredSize.Height, Math.Max(dot.DesiredSize.Height, title.DesiredSize.Height));
        return new Size(_total, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        var x = Math.Max(0, (finalSize.Width - _total) / 2);
        x = Place(Children[0], x, _identity, finalSize.Height);
        if (_title > 0)
        {
            x = Place(Children[1], x + Spacing, Children[1].DesiredSize.Width, finalSize.Height);
            Place(Children[2], x + Spacing, _title, finalSize.Height);
        }
        else
        {
            Place(Children[1], x, 0, finalSize.Height);
            Place(Children[2], x, 0, finalSize.Height);
        }
        return finalSize;
    }

    private static double Place(UIElement child, double x, double width, double height)
    {
        var y = Math.Max(0, (height - child.DesiredSize.Height) / 2);
        child.Arrange(new Rect(x, y, width, child.DesiredSize.Height));
        return x + width;
    }
}
