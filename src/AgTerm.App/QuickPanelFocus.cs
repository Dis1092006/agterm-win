using AgTerm.ControlServer;

namespace AgTerm.App;

public static class QuickPanelFocus
{
    public static void ActivationCompleted(HeadlessQuickTerminal quick, bool ownsForeground,
        Action<string> trace, Action<string> report)
    {
        if (!ownsForeground)
        {
            const string message = "Windows denied activating the quick terminal.";
            if (quick.DismissesOnFocusLoss) report(message);
            else trace(message);
        }
        quick.SetKeyOwnership(ownsForeground);
    }

    public static void RestoreDenied(Action<string> trace) =>
        trace("Windows denied returning focus to the previous foreground window.");
}
