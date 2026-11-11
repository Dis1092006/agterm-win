using System.ComponentModel;
using System.Diagnostics;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgTerm.App;

/// <summary>IHtmlBrowser for the window (upstream SystemBrowser and SystemHtmlSharing): the prompt is a
/// ContentDialog in the window's dialog queue, URLs go to the shell and files to Explorer.</summary>
public sealed class AppHtmlBrowser(Func<XamlRoot?> root, ContentDialogCoordinator dialogs) : IHtmlBrowser
{
    public Action? Confirm(Uri url, Action<bool> done)
    {
        if (root() is not { } xamlRoot) return null;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Open {HtmlSource.Origin(url) ?? url.AbsoluteUri} in your browser?",
            Content = new TextBlock
            {
                Text = url.AbsoluteUri,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            },
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            // cancel is the Enter default, so typing meant for the page cannot approve a surprise prompt
            DefaultButton = ContentDialogButton.Close,
        };
        var dismissed = false;
        _ = ShowAsync();
        return () =>
        {
            dismissed = true;
            dialog.Hide();
        };

        async Task ShowAsync()
        {
            try
            {
                await dialogs.RunAsync(async () =>
                {
                    if (dismissed) return false;
                    var approved = await dialog.ShowAsync() == ContentDialogResult.Primary;
                    if (!dismissed) done(approved);
                    return approved;
                });
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                // the window is closing
            }
        }
    }

    public bool Open(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public void Reveal(string path) =>
        Process.Start(WindowsExplorer.StartInfo(HudLinkPolicy.ExplorerSelectArguments(path)))?.Dispose();
}
