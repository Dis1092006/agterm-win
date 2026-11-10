using AgTerm.Core.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AgTerm.App;

/// <summary>Upstream's standard About panel as a dialog: the app icon, the name, the running version,
/// the links and the copyright.</summary>
internal static class AboutDialog
{
    private const int IconFrameSize = 128;
    private const double IconSize = 64;

    /// <param name="darkChrome">The dialog is dark: it shows the icon drawn for a dark taskbar,
    /// whose window outline stays visible on a dark background.</param>
    public static async Task<ContentDialog> CreateAsync(XamlRoot xamlRoot, bool darkChrome)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = AboutPanel.Name, FontSize = 20, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = AboutPanel.VersionLine(AppIdentity.Running(typeof(AboutDialog).Assembly)),
            IsTextSelectionEnabled = true,
        });
        text.Children.Add(Link(AboutPanel.UpstreamText, AboutPanel.UpstreamUrl));
        text.Children.Add(Link(AboutPanel.RepositoryText, AboutPanel.RepositoryUrl));
        text.Children.Add(new TextBlock { Text = AboutPanel.Copyright, Margin = new Thickness(0, 8, 0, 0) });

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        if (await IconAsync(darkChrome ? TaskbarAppIcon.ForDarkTaskbar : TaskbarAppIcon.ForLightTaskbar) is { } icon)
            content.Children.Add(icon);
        content.Children.Add(text);

        return new ContentDialog
        {
            XamlRoot = xamlRoot,
            Content = content,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
    }

    private static HyperlinkButton Link(string text, string url) => new()
    {
        Content = text,
        NavigateUri = new Uri(url),
        Padding = new Thickness(0),
    };

    private static async Task<Image?> IconAsync(string resource)
    {
        using var stream = typeof(AboutDialog).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var ico = buffer.ToArray();
        if (IconDirectory.FrameFor(ico, IconFrameSize) is not { } frame)
            return null;

        // every frame of the app icon is a PNG, which BitmapImage reads as it is
        var png = new MemoryStream(ico, frame.Offset, frame.Length, writable: false);
        var image = new BitmapImage();
        await image.SetSourceAsync(png.AsRandomAccessStream());
        return new Image
        {
            Source = image,
            Width = IconSize,
            Height = IconSize,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }
}
