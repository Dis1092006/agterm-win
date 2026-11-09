using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record TerminalAskButtonView(IReadOnlyList<AskLabelRun> Runs, bool Destructive);

/// <summary>A question's text field: Mode is "line" or "multiline".</summary>
public sealed record TerminalAskInputView(string Mode, string Value, string? Placeholder);

public sealed record TerminalAskChoiceView(string Label, string? Subtitle, bool Checked);

/// <summary>A question's list: radio items, or checkboxes when Multiple. Cursor is the row the
/// keyboard acts on.</summary>
public sealed record TerminalAskChoicesView(bool Multiple, IReadOnlyList<TerminalAskChoiceView> Items, int Cursor)
{
    public bool Equals(TerminalAskChoicesView? other) =>
        other is not null && Multiple == other.Multiple && Cursor == other.Cursor && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Multiple, Cursor, Items.Count);
}

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
    TerminalAskInputView? Input = null,
    TerminalAskChoicesView? Choices = null);

public abstract record TerminalAskResolution
{
    public sealed record None : TerminalAskResolution;
    public sealed record Redraw(TerminalAskView View) : TerminalAskResolution;
    /// <summary>The list changed: redraw, and save Draft, a copy, as the session's draft.</summary>
    public sealed record Drafted(TerminalAskView View, AskDraft Draft) : TerminalAskResolution;
    public sealed record Resolve(Guid SessionId, string AskId, ControlAskResult Result) : TerminalAskResolution;
}

/// <summary>The keyboard state of the window's terminal question. The page only reports intents;
/// the highlight lives here, so both styles share AskNavigation. The draft (field text, list cursor
/// and checked items) lives here too: the store copy is written off the UI thread and can lag, so
/// the answer reads this one. UI thread only.</summary>
public sealed class TerminalAskNavigator
{
    private TerminalAskPlacement? _placement;
    private AskNavigation? _navigation;
    private string? _draftAskId;
    private AskDraft _draft = new(null);

    public string? CurrentAskId => _placement?.Ask.Id;

    public Guid? CurrentSessionId => _placement?.SessionId;

    /// <summary>A copy of the drawn question's draft for the store, null when nothing is drawn.</summary>
    public AskDraft? DraftSnapshot() => _placement is null ? null : _draft.Copy();

    /// <summary>The highlight survives a redraw of the same question and resets for a new one or
    /// after the question was hidden, as upstream's view state does. The draft survives a hide of
    /// the same question and starts from the placement's draft for a new one.</summary>
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
            _draft = placement.Draft?.Copy() ?? new AskDraft(placement.Ask.Form);
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
        _draft.Text = text;
        return true;
    }

    public TerminalAskResolution Apply(string askId, AskIntent intent)
    {
        if (_placement is not { } placement || _navigation is null || placement.Ask.Id != askId)
            return new TerminalAskResolution.None();
        if (_draft.Apply(intent))
            return new TerminalAskResolution.Drafted(View(), _draft.Copy());
        return _navigation.Apply(intent) switch
        {
            AskIntentOutcome.Moved => new TerminalAskResolution.Redraw(View()),
            AskIntentOutcome.Answer answer when placement.Ask.Answer(answer.Index, _draft) is { } result =>
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
                ? new TerminalAskInputView(input.Field.Mode == ControlAskInputMode.Multiline ? "multiline" : "line", _draft.Text, input.Field.Placeholder)
                : null,
            ask.Form is AskForm.Choices { List: var list }
                ? new TerminalAskChoicesView(
                    list.Multiple,
                    list.Items.Select((item, index) => new TerminalAskChoiceView(item.Label, item.Subtitle, _draft.IsChecked(index))).ToArray(),
                    _draft.Cursor)
                : null);
    }
}
