using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AgTerm.App;

internal static class PasteConfirmationDialog
{
    /// <summary>Cancel is the default button, so Enter and Esc both paste nothing.</summary>
    public static ContentDialog Create(XamlRoot root, string text, bool bracketed) => new()
    {
        XamlRoot = root,
        Title = PasteConfirmation.Title,
        PrimaryButtonText = PasteConfirmation.PasteButton,
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close,
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = PasteConfirmation.Message(bracketed), TextWrapping = TextWrapping.Wrap },
                new ScrollViewer
                {
                    MaxHeight = 240,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = PasteConfirmation.Preview(text),
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        IsTextSelectionEnabled = true,
                    },
                },
            },
        },
    };
}
