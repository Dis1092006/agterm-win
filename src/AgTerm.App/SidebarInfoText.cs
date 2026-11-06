using AgTerm.Xterm;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace AgTerm.App;

/// <summary>Draws a session info line's runs into a TextBlock. A trimmed line shows its whole text in a
/// tooltip, as upstream's info cell does.</summary>
public static class SidebarInfoText
{
    // object, not the list type: the property store holds any .NET object as it is
    public static readonly DependencyProperty RunsProperty = DependencyProperty.RegisterAttached(
        "Runs",
        typeof(object),
        typeof(SidebarInfoText),
        new PropertyMetadata(null, OnRunsChanged));

    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    public static IReadOnlyList<SidebarInfoRun>? GetRuns(DependencyObject target) =>
        (IReadOnlyList<SidebarInfoRun>?)target.GetValue(RunsProperty);

    public static void SetRuns(DependencyObject target, IReadOnlyList<SidebarInfoRun>? value) =>
        target.SetValue(RunsProperty, value);

    private static void OnRunsChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBlock block) return;
        block.IsTextTrimmedChanged -= OnTrimmedChanged;
        block.IsTextTrimmedChanged += OnTrimmedChanged;
        block.Inlines.Clear();
        foreach (var run in args.NewValue as IReadOnlyList<SidebarInfoRun> ?? [])
            block.Inlines.Add(InlineOf(run));
        UpdateToolTip(block);
    }

    private static Run InlineOf(SidebarInfoRun run)
    {
        var inline = new Run
        {
            Text = run.Text,
            Foreground = SidebarRowViewModel.BrushFrom(run.Color, (byte)Math.Round(run.Opacity * 255)),
        };
        if (run.Bold) inline.FontWeight = FontWeights.SemiBold;
        if (run.Italic) inline.FontStyle = FontStyle.Italic;
        if (run.Code) inline.FontFamily = CodeFont;
        return inline;
    }

    private static void OnTrimmedChanged(TextBlock block, IsTextTrimmedChangedEventArgs args) => UpdateToolTip(block);

    private static void UpdateToolTip(TextBlock block) =>
        ToolTipService.SetToolTip(block, block.IsTextTrimmed
            ? string.Concat((GetRuns(block) ?? []).Select(run => run.Text))
            : null);
}
