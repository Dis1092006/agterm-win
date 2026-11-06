using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AgTerm.App;

/// <summary>The theme foreground of a colored sidebar row, put on its text and icons. A null value
/// clears the local brush instead of setting one, so a default row keeps what its style and the
/// list container give it, including the container's selected and hover foregrounds.</summary>
public static class SidebarRowBrushes
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground",
        typeof(Brush),
        typeof(SidebarRowBrushes),
        new PropertyMetadata(null, OnForegroundChanged));

    public static Brush? GetForeground(DependencyObject target) => (Brush?)target.GetValue(ForegroundProperty);

    public static void SetForeground(DependencyObject target, Brush? value) => target.SetValue(ForegroundProperty, value);

    private static void OnForegroundChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var property = target switch
        {
            TextBlock => TextBlock.ForegroundProperty,
            IconElement => IconElement.ForegroundProperty,
            Shape => Shape.StrokeProperty,
            _ => null,
        };
        if (property is null)
            return;
        Put(target, property, args.NewValue);
    }

    /// <summary>The fill of a filled glyph, cleared the same way.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill",
        typeof(Brush),
        typeof(SidebarRowBrushes),
        new PropertyMetadata(null, OnFillChanged));

    public static Brush? GetFill(DependencyObject target) => (Brush?)target.GetValue(FillProperty);

    public static void SetFill(DependencyObject target, Brush? value) => target.SetValue(FillProperty, value);

    private static void OnFillChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is Shape)
            Put(target, Shape.FillProperty, args.NewValue);
    }

    private static void Put(DependencyObject target, DependencyProperty property, object? value)
    {
        if (value is Brush brush)
            target.SetValue(property, brush);
        else
            target.ClearValue(property);
    }
}
