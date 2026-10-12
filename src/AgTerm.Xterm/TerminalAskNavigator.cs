using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record TerminalAskButtonView(IReadOnlyList<AskLabelRun> Runs, bool Destructive);

/// <summary>What the page draws for the window's terminal question.</summary>
public sealed record TerminalAskView(
    string AskId,
    string? PaneId,
    IReadOnlyList<string> CoveredPaneIds,
    string Title,
    string? Message,
    IReadOnlyList<TerminalAskButtonView> Buttons,
    int? Highlighted,
    string Align,
    int? Width);

public abstract record TerminalAskResolution
{
    public sealed record None : TerminalAskResolution;
    public sealed record Redraw(TerminalAskView View) : TerminalAskResolution;
    public sealed record Resolve(Guid SessionId, string AskId, ControlAskResult Result) : TerminalAskResolution;
}

/// <summary>The keyboard state of the window's terminal question. The page only reports intents;
/// the highlight lives here, so both styles share AskNavigation. UI thread only.</summary>
public sealed class TerminalAskNavigator
{
    private TerminalAskPlacement? _placement;
    private AskNavigation? _navigation;

    public string? CurrentAskId => _placement?.Ask.Id;

    /// <summary>The highlight survives a redraw of the same question and resets for a new one or
    /// after the question was hidden, as upstream's view state does.</summary>
    public TerminalAskView? Present(TerminalAskPlacement? placement)
    {
        if (placement is null)
        {
            _placement = null;
            _navigation = null;
            return null;
        }
        if (_placement?.Ask.Id != placement.Ask.Id || _navigation is null)
            _navigation = AskNavigation.For(placement.Ask);
        _placement = placement;
        return View();
    }

    public TerminalAskResolution Apply(string askId, AskIntent intent)
    {
        if (_placement is not { } placement || _navigation is null || placement.Ask.Id != askId)
            return new TerminalAskResolution.None();
        return _navigation.Apply(intent) switch
        {
            AskIntentOutcome.Moved => new TerminalAskResolution.Redraw(View()),
            AskIntentOutcome.Answer answer => new TerminalAskResolution.Resolve(placement.SessionId, askId,
                ControlAskResult.Answered(placement.Ask.Buttons[answer.Index].Id, placement.Ask.Buttons[answer.Index].Label, answer.Index)),
            AskIntentOutcome.Escape => new TerminalAskResolution.Resolve(placement.SessionId, askId, ControlAskResult.Escaped()),
            _ => new TerminalAskResolution.None(),
        };
    }

    private TerminalAskView View()
    {
        var placement = _placement!;
        var ask = placement.Ask;
        return new TerminalAskView(
            ask.Id,
            placement.PaneId,
            placement.CoveredPaneIds,
            ask.Title,
            string.IsNullOrEmpty(ask.Message) ? null : ask.Message,
            ask.Buttons.Select(button =>
            {
                var destructive = AskIds.Same(button.Id, ask.DestructiveId);
                return new TerminalAskButtonView(AskLabel.Runs(button.Label, button.Hotkey, destructive), destructive);
            }).ToArray(),
            _navigation!.Highlighted,
            ask.Align switch
            {
                ControlAskAlignment.Left => "left",
                ControlAskAlignment.Center => "center",
                _ => "right",
            },
            ask.Width);
    }
}
