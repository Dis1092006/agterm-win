using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgTerm.App;

public sealed partial class SessionStatusGlyph : UserControl
{
    public SessionStatusGlyph()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public static readonly DependencyProperty ShapeDataProperty =
        DependencyProperty.Register(nameof(ShapeData), typeof(string), typeof(SessionStatusGlyph), new PropertyMetadata(""));

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(nameof(ColorHex), typeof(string), typeof(SessionStatusGlyph), new PropertyMetadata(null));

    public static readonly DependencyProperty ShownProperty =
        DependencyProperty.Register(nameof(Shown), typeof(bool), typeof(SessionStatusGlyph),
            new PropertyMetadata(false, (sender, args) =>
                ((SessionStatusGlyph)sender).Visibility = (bool)args.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public string ShapeData
    {
        get => (string)GetValue(ShapeDataProperty);
        set => SetValue(ShapeDataProperty, value);
    }

    public string? ColorHex
    {
        get => (string?)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public bool Shown
    {
        get => (bool)GetValue(ShownProperty);
        set => SetValue(ShownProperty, value);
    }
}
