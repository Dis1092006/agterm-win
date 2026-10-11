using System.Text;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>The session or pane a GUI question was anchored to when it opened. PaneIdentity
/// follows the pane through split-survivor promotion.</summary>
public sealed record AskAnchor(Guid SessionId, OverlayPane? Pane = null, Guid? PaneIdentity = null);

/// <summary>One question waiting for an answer in its owning session or window. Anchor is set
/// only for a GUI question opened with a session target; a terminal question keeps its placement
/// on the session.</summary>
public sealed record PendingAsk(
    string Id,
    string Title,
    string? Message,
    IReadOnlyList<ControlAskButton> Buttons,
    string? DefaultId,
    string? DestructiveId,
    ControlAskStyle Style,
    ControlAskAlignment Align,
    int? Width,
    AskAnchor? Anchor = null);

public static class AskIds
{
    /// <summary>Swift String equality treats canonically equivalent strings as equal; ordinal
    /// comparison does not.</summary>
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(a.Normalize(NormalizationForm.FormC), b.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);
}

/// <summary>What the user did in a dialog, in the dialog's own terms.</summary>
public abstract record AskIntent
{
    public sealed record Forward : AskIntent;
    public sealed record Backward : AskIntent;
    public sealed record Activate : AskIntent;
    public sealed record Cancel : AskIntent;
    public sealed record Hotkey(string Letter) : AskIntent;
    public sealed record Press(int Index) : AskIntent;
}

public abstract record AskIntentOutcome
{
    public sealed record None : AskIntentOutcome;
    public sealed record Moved(int Highlighted) : AskIntentOutcome;
    public sealed record Answer(int Index) : AskIntentOutcome;
    public sealed record Escape : AskIntentOutcome;
}

/// <summary>Port of upstream AskNavigation: keyboard selection in caller button order.</summary>
public sealed class AskNavigation
{
    private readonly IReadOnlyList<ControlAskButton> _buttons;

    public AskNavigation(IReadOnlyList<ControlAskButton> buttons, string? defaultId = null, string? destructiveId = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        _buttons = buttons;
        Highlighted = IndexWhere(button => AskIds.Same(button.Id, defaultId))
            ?? IndexWhere(button => !AskIds.Same(button.Id, destructiveId))
            ?? (buttons.Count > 0 ? 0 : null);
    }

    public static AskNavigation For(PendingAsk ask) => new(ask.Buttons, ask.DefaultId, ask.DestructiveId);

    /// <summary>Null only for an empty button list.</summary>
    public int? Highlighted { get; private set; }

    public void MoveForward()
    {
        if (_buttons.Count == 0) return;
        Highlighted = Highlighted is { } index ? (index + 1) % _buttons.Count : 0;
    }

    public void MoveBackward()
    {
        if (_buttons.Count == 0) return;
        Highlighted = Highlighted is { } index ? (index + _buttons.Count - 1) % _buttons.Count : _buttons.Count - 1;
    }

    public int? Activate() => Highlighted;

    public int? Hotkey(string letter)
    {
        var key = letter.ToLowerInvariant();
        return IndexWhere(button => button.Hotkey?.ToLowerInvariant() == key);
    }

    public AskIntentOutcome Apply(AskIntent intent)
    {
        switch (intent)
        {
            case AskIntent.Forward:
                MoveForward();
                return Moved();
            case AskIntent.Backward:
                MoveBackward();
                return Moved();
            case AskIntent.Activate:
                return Answered(Activate());
            case AskIntent.Hotkey hotkey:
                return Answered(Hotkey(hotkey.Letter));
            case AskIntent.Press press:
                return Answered(press.Index >= 0 && press.Index < _buttons.Count ? press.Index : null);
            case AskIntent.Cancel:
                return new AskIntentOutcome.Escape();
            default:
                throw new ArgumentOutOfRangeException(nameof(intent));
        }
    }

    private AskIntentOutcome Moved() =>
        Highlighted is { } index ? new AskIntentOutcome.Moved(index) : new AskIntentOutcome.None();

    private static AskIntentOutcome Answered(int? index) =>
        index is { } value ? new AskIntentOutcome.Answer(value) : new AskIntentOutcome.None();

    private int? IndexWhere(Func<ControlAskButton, bool> predicate)
    {
        for (var i = 0; i < _buttons.Count; i++)
            if (predicate(_buttons[i])) return i;
        return null;
    }
}
