using System.Globalization;
using System.Text;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>The session or pane a GUI question was anchored to when it opened. PaneIdentity
/// follows the pane through split-survivor promotion.</summary>
public sealed record AskAnchor(Guid SessionId, OverlayPane? Pane = null, Guid? PaneIdentity = null);

/// <summary>One question waiting for an answer in its owning session or window. Anchor is set
/// only for a GUI question opened with a session target; a terminal question keeps its placement
/// on the session. Form is the text field shown above the buttons, absent for a plain button
/// ask. CancelId names the button that resolves escaped, as Esc does.</summary>
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
    AskAnchor? Anchor = null,
    AskForm? Form = null,
    string? CancelId = null)
{
    /// <summary>The outcome of pressing the button at index with draft as the form's input: the
    /// cancel button escapes with no payload, any other answers with the field text. Null for an
    /// index outside the buttons.</summary>
    public ControlAskResult? Answer(int index, AskDraft? draft)
    {
        if (index < 0 || index >= Buttons.Count) return null;
        var button = Buttons[index];
        if (AskIds.Same(button.Id, CancelId)) return ControlAskResult.Escaped();
        var answered = ControlAskResult.Answered(button.Id, button.Label, index);
        return Form switch
        {
            AskForm.Input => answered with { Text = (draft ?? new AskDraft(Form)).Text },
            AskForm.Choices => answered with { Selected = (draft ?? new AskDraft(Form)).Selected },
            _ => answered,
        };
    }
}

/// <summary>The optional content an ask shows between its message and its buttons.</summary>
public abstract record AskForm
{
    private AskForm()
    {
    }

    public sealed record Input(AskInputForm Field) : AskForm;

    public sealed record Choices(AskChoiceForm List) : AskForm;
}

/// <summary>An ask's radio or checkbox list. Checked lists the ids selected on open; a radio
/// list holds at most one.</summary>
public sealed record AskChoiceForm(IReadOnlyList<ControlAskChoice> Items, bool Multiple = false, IReadOnlyList<string>? Checked = null)
{
    public IReadOnlyList<string> Checked { get; init; } = Checked ?? [];

    public bool Equals(AskChoiceForm? other) =>
        other is not null && Multiple == other.Multiple && Items.SequenceEqual(other.Items) && Checked.SequenceEqual(other.Checked);

    public override int GetHashCode() => HashCode.Combine(Multiple, Items.Count, Checked.Count);
}

/// <summary>An ask's text field: its mode, initial text and the hint shown while it is empty.</summary>
public sealed record AskInputForm(ControlAskInputMode Mode, string Value = "", string? Placeholder = null)
{
    /// <summary>The cap on the prefill and on the field's content, in UTF-8 bytes.</summary>
    public const int MaxTextBytes = 65_536;

    /// <summary>A C0 control or DEL in text; newline and tab are allowed in a multiline field.</summary>
    public static bool HasForbiddenControls(string text, ControlAskInputMode mode) =>
        text.Any(c => (c < 0x20 || c == 0x7F) && (mode == ControlAskInputMode.Line || (c != '\n' && c != '\t')));

    /// <summary>text cut at a character boundary so it fits MaxTextBytes. A character is a text
    /// element, as Swift's Character is, so a surrogate pair or a combining sequence is never split.</summary>
    public static string Clamped(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= MaxTextBytes) return text;
        var bytes = 0;
        var kept = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            bytes += Encoding.UTF8.GetByteCount(element);
            if (bytes > MaxTextBytes) break;
            kept += element.Length;
        }
        return text[..kept];
    }
}

/// <summary>The rules for the GUI dialog's text box. A WinUI TextBox keeps each line break as \r,
/// so a multiline field converts on the way in and out. An edit over the cap or with a forbidden
/// control is refused whole.</summary>
public static class AskFieldText
{
    public static string FromTextBox(string text, ControlAskInputMode mode) =>
        mode == ControlAskInputMode.Multiline ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;

    public static string ToTextBox(string text, ControlAskInputMode mode) =>
        mode == ControlAskInputMode.Multiline ? text.Replace('\n', '\r') : text;

    public static bool Accepts(string text, ControlAskInputMode mode) =>
        !AskInputForm.HasForbiddenControls(text, mode) && Encoding.UTF8.GetByteCount(text) <= AskInputForm.MaxTextBytes;

    /// <summary>The text a paste inserts, or null when it is refused. A Windows clipboard CRLF is one
    /// newline in a multiline field; a lone \r is still refused.</summary>
    public static string? PasteInsertion(string clipboard, ControlAskInputMode mode)
    {
        var text = mode == ControlAskInputMode.Multiline ? clipboard.Replace("\r\n", "\n") : clipboard;
        return AskInputForm.HasForbiddenControls(text, mode) ? null : text;
    }
}

/// <summary>The user's unsubmitted input, kept by the ask's slot owner so hiding the dialog keeps it.</summary>
public sealed class AskDraft
{
    private string _text;
    private readonly string[] _ids;
    private readonly string[] _keys;
    private readonly bool _multiple;
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);

    /// <summary>Starts from the form's initial values: the prefill, the checked ids, and a radio
    /// list's first item when nothing is checked. The cursor starts on the first checked item in
    /// caller order. Ids are compared in NFC, as Swift compares strings.</summary>
    public AskDraft(AskForm? form)
    {
        _text = form is AskForm.Input input ? AskInputForm.Clamped(input.Field.Value) : "";
        if (form is not AskForm.Choices { List: var list })
        {
            _ids = [];
            _keys = [];
            return;
        }
        _ids = list.Items.Select(item => item.Id).ToArray();
        _keys = _ids.Select(Key).ToArray();
        _multiple = list.Multiple;
        var initial = list.Multiple ? list.Checked : list.Checked.Take(1).DefaultIfEmpty(_ids.FirstOrDefault());
        foreach (var id in initial)
            if (id is not null) _checked.Add(Key(id));
        Cursor = Math.Max(0, Array.FindIndex(_keys, _checked.Contains));
    }

    /// <summary>The input field content, held within AskInputForm.MaxTextBytes.</summary>
    public string Text
    {
        get => _text;
        set => _text = AskInputForm.Clamped(value ?? "");
    }

    /// <summary>The index of the list item keyboard actions apply to.</summary>
    public int Cursor { get; private set; }

    /// <summary>The checked ids in caller order, as the caller spelled them in the list.</summary>
    public IReadOnlyList<string> Selected => _ids.Where((_, index) => _checked.Contains(_keys[index])).ToArray();

    /// <summary>Steps the cursor by offset, stopping at the list ends.</summary>
    public void MoveCursor(int offset)
    {
        if (_keys.Length == 0) return;
        Cursor = Math.Clamp(Cursor + offset, 0, _keys.Length - 1);
    }

    /// <summary>Moves the cursor to index, then picks a radio item or flips a checkbox. An index
    /// outside the list changes nothing.</summary>
    public void Toggle(int index)
    {
        if (index < 0 || index >= _keys.Length) return;
        Cursor = index;
        var key = _keys[index];
        if (!_multiple)
        {
            _checked.Clear();
            _checked.Add(key);
            return;
        }
        if (!_checked.Remove(key)) _checked.Add(key);
    }

    public void ToggleCursor() => Toggle(Cursor);

    public bool IsChecked(int index) => index >= 0 && index < _keys.Length && _checked.Contains(_keys[index]);

    /// <summary>Applies a list intent: Up and Down move the cursor, Toggle flips the cursor's
    /// item and ToggleAt the clicked one. False for any other intent, for a row outside the list
    /// and for a draft without a list, so the buttons take it.</summary>
    public bool Apply(AskIntent intent)
    {
        if (_keys.Length == 0) return false;
        switch (intent)
        {
            case AskIntent.Up:
                MoveCursor(-1);
                return true;
            case AskIntent.Down:
                MoveCursor(1);
                return true;
            case AskIntent.Toggle:
                ToggleCursor();
                return true;
            case AskIntent.ToggleAt at when at.Index >= 0 && at.Index < _keys.Length:
                Toggle(at.Index);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A snapshot another thread can read while this draft keeps changing.</summary>
    public AskDraft Copy()
    {
        var copy = new AskDraft(_ids, _keys, _multiple) { _text = _text, Cursor = Cursor };
        copy._checked.UnionWith(_checked);
        return copy;
    }

    /// <summary>Takes the text, cursor and checked items of a draft of the same form.</summary>
    public void CopyFrom(AskDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _text = source._text;
        Cursor = source.Cursor;
        _checked.Clear();
        _checked.UnionWith(source._checked);
    }

    private AskDraft(string[] ids, string[] keys, bool multiple)
    {
        _text = "";
        _ids = ids;
        _keys = keys;
        _multiple = multiple;
    }

    private static string Key(string id) => id.Normalize(NormalizationForm.FormC);
}

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
    /// <summary>Up and Down move a list's cursor, or the button highlight when there is no list.</summary>
    public sealed record Up : AskIntent;
    public sealed record Down : AskIntent;
    /// <summary>Space: toggles the list item under the cursor.</summary>
    public sealed record Toggle : AskIntent;
    /// <summary>A click on the list row at Index.</summary>
    public sealed record ToggleAt(int Index) : AskIntent;
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
            case AskIntent.Forward or AskIntent.Down:
                MoveForward();
                return Moved();
            case AskIntent.Backward or AskIntent.Up:
                MoveBackward();
                return Moved();
            case AskIntent.Toggle or AskIntent.ToggleAt:
                return new AskIntentOutcome.None();
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
