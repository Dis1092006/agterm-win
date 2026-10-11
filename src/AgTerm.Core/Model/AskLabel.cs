namespace AgTerm.Core.Model;

public sealed record AskLabelRun(string Text, bool Underline);

public static class AskLabel
{
    /// <summary>Port of upstream AskDialogView.buttonLabel: the first occurrence of the hotkey in
    /// the label is underlined, ignoring case; a label without it gets " (K)" appended. The terminal
    /// style marks a destructive button with a "! " prefix, which color alone cannot carry there.</summary>
    public static IReadOnlyList<AskLabelRun> Runs(string label, string? hotkey, bool destructivePrefix)
    {
        var runs = new List<AskLabelRun>();
        if (destructivePrefix) runs.Add(new AskLabelRun("! ", false));
        var at = hotkey is null ? -1 : label.IndexOf(hotkey, StringComparison.OrdinalIgnoreCase);
        if (hotkey is null)
        {
            runs.Add(new AskLabelRun(label, false));
        }
        else if (at >= 0)
        {
            if (at > 0) runs.Add(new AskLabelRun(label[..at], false));
            runs.Add(new AskLabelRun(label.Substring(at, hotkey.Length), true));
            if (at + hotkey.Length < label.Length) runs.Add(new AskLabelRun(label[(at + hotkey.Length)..], false));
        }
        else
        {
            runs.Add(new AskLabelRun(label + " (", false));
            runs.Add(new AskLabelRun(hotkey.ToUpperInvariant(), true));
            runs.Add(new AskLabelRun(")", false));
        }
        return runs;
    }
}
