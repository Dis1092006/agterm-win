using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record TerminalAskButtonView(IReadOnlyList<AskLabelRun> Runs, bool Destructive);

/// <summary>A question's text field: Mode is "line" or "multiline".</summary>
public sealed record TerminalAskInputView(string Mode, string Value, string? Placeholder);

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
    int? Width,
    TerminalAskInputView? Input = null);

public abstract record TerminalAskResolution
{
    public sealed record None : TerminalAskResolution;
    public sealed record Redraw(TerminalAskView View) : TerminalAskResolution;
    public sealed record Resolve(Guid SessionId, string AskId, ControlAskResult Result) : TerminalAskResolution;
}

/// <summary>The keyboard state of the window's terminal question. The page only reports intents;
/// the highlight lives here, so both styles share AskNavigation. The field text lives here too:
/// the store copy is written off the UI thread and can lag, so the answer reads this one. UI
/// thread only.</summary>
public sealed class TerminalAskNavigator
{
    private TerminalAskPlacement? _placement;
    private AskNavigation? _navigation;
    private string? _draftAskId;
    private string _draftText = "";

    public string? CurrentAskId => _placement?.Ask.Id;

    public Guid? CurrentSessionId => _placement?.SessionId;

    /// <summary>The highlight survives a redraw of the same question and resets for a new one or
    /// after the question was hidden, as upstream's view state does. The field text survives a
    /// hide of the same question and starts from the placement's draft for a new one.</summary>
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
        if (_draftAskId != placement.Ask.Id)
        {
            _draftAskId = placement.Ask.Id;
            _draftText = placement.DraftText ?? new AskDraft(placement.Ask.Form).Text;
        }
        _placement = placement;
        return View();
    }

    /// <summary>Takes the page's field text for the drawn question. Refuses a stale id, a question
    /// without a field, a forbidden control and text over the cap, keeping the old text.</summary>
    public bool UpdateDraft(string askId, string text)
    {
        if (_placement is not { Ask: { Form: AskForm.Input input } ask } || ask.Id != askId
            || AskInputForm.HasForbiddenControls(text, input.Field.Mode)
            || Encoding.UTF8.GetByteCount(text) > AskInputForm.MaxTextBytes)
            return false;
        _draftText = text;
        return true;
    }

    public TerminalAskResolution Apply(string askId, AskIntent intent)
    {
        if (_placement is not { } placement || _navigation is null || placement.Ask.Id != askId)
            return new TerminalAskResolution.None();
        return _navigation.Apply(intent) switch
        {
            AskIntentOutcome.Moved => new TerminalAskResolution.Redraw(View()),
            AskIntentOutcome.Answer answer when placement.Ask.Answer(answer.Index, new AskDraft(placement.Ask.Form) { Text = _draftText }) is { } result =>
                new TerminalAskResolution.Resolve(placement.SessionId, askId, result),
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
            ask.Width,
            ask.Form is AskForm.Input input
                ? new TerminalAskInputView(input.Field.Mode == ControlAskInputMode.Multiline ? "multiline" : "line", _draftText, input.Field.Placeholder)
                : null);
    }
}
