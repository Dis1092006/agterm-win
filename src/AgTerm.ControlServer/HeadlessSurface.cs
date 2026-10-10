using AgTerm.Core.Model;

namespace AgTerm.ControlServer;

/// <summary>
/// The terminal-engine seam of the headless host. Upstream's surface-I/O control arms reach into a
/// live GhosttySurfaceView (inject keystrokes, read the screen buffer, read the selection, run a
/// libghostty binding action); the headless host has no terminal engine, so those arms go through
/// this interface instead. Members mirror exactly what the ControlServer+SurfaceIO / +AppCommands
/// arms call on the Swift surface:
///  - Inject         ↔ GhosttySurfaceView.inject(text:)        (session.type / quick.type)
///  - ReadScreenText ↔ readScreenText(all:lines:)              (session.text / quick.text)
///  - SelectedText   ↔ readSelection()                         (session.copy)
///  - IsRealized     ↔ GhosttySurfaceView.isRealized            (session.copy / session.selectall)
///  - SelectAll      ↔ performBindingAction("select_all")      (session.selectall)
///  - Paste          ↔ performBindingAction("paste_from_clipboard") (session.paste)
///  - PerformBindingAction ↔ performBindingAction(_:) with a Bool result (font.inc/dec/reset)
/// A false Inject / PerformBindingAction or a null ReadScreenText models "the view exists but its
/// libghostty surface isn't realized yet" — the arms map it to the upstream error strings.
/// </summary>
public interface IHeadlessSurface : ITerminalSurface
{
    /// <summary>Send text as keystrokes to the pane's pty. False = surface not realized.</summary>
    bool Inject(string text);

    /// <summary>The pane's buffer as plain text: the visible screen, the full screen + scrollback
    /// with all, or the last lines lines. Null = surface not realized (a realized blank screen
    /// reads as "").</summary>
    string? ReadScreenText(bool all, int? lines);

    /// <summary>The current selection text, or null when nothing is selected.</summary>
    string? SelectedText { get; }

    /// <summary>Select the entire terminal buffer (the select_all binding action).</summary>
    void SelectAll();

    /// <summary>Paste the system clipboard into the pane. False = surface not realized.</summary>
    bool Paste();

    /// <summary>Run a named libghostty binding action (the font arms). False = not realized.</summary>
    bool PerformBindingAction(string action);

    /// <summary>False while the pane has no terminal behind it (pending realize, output stopped).
    /// session.selectall and session.copy check it; Paste reports the same state by returning false.</summary>
    bool IsRealized => true;
}

/// <summary>
/// The recording surface tests and the Phase-2 E2E suite attach to sessions (the headless host never
/// fabricates surfaces itself — Session.Surface / SplitSurface / ScratchSurface / OverlaySurface and
/// the quick-terminal slot are populated externally). Settable screen text / selection model what a
/// real terminal would show; injected text and binding actions are recorded for assertions.
/// </summary>
public sealed class FakeSurface : IHeadlessSurface
{
    /// <summary>The stable spawn token (the shell's baked AGTERM_PANE_ID) used by the --pane-id
    /// resolution in session.status / session.restore. Null = a shell that exports none.</summary>
    public string? PaneToken { get; set; }

    /// <summary>False models a mounted-but-unrealized libghostty surface: Inject / Paste /
    /// PerformBindingAction return false and ReadScreenText returns null.</summary>
    public bool Realized { get; set; } = true;

    /// <summary>What the terminal screen "shows". ReadScreenText returns it (last-N-lines trimmed
    /// for a --lines read). The fake keeps no separate scrollback: --all reads the same text.</summary>
    public string ScreenText { get; set; } = "";

    /// <summary>The current selection; null = no selection ("no selection" over the wire).</summary>
    public string? Selection { get; set; }

    /// <summary>Every Inject payload, in order.</summary>
    public List<string> Injected { get; } = new();

    /// <summary>Every PerformBindingAction name, in order (font actions).</summary>
    public List<string> BindingActions { get; } = new();

    public int TeardownCount { get; private set; }

    public int PromoteToPrimaryCount { get; private set; }

    public int PasteCount { get; private set; }

    public int SelectAllCount { get; private set; }

    public string? SelectedText => Selection;

    public bool IsRealized => Realized;

    public bool Inject(string text)
    {
        if (!Realized) return false;
        Injected.Add(text);
        return true;
    }

    public string? ReadScreenText(bool all, int? lines)
    {
        if (!Realized) return null;
        if (lines is not { } n) return ScreenText;
        var split = ScreenText.Split('\n');
        return string.Join("\n", split.Skip(Math.Max(0, split.Length - n)));
    }

    /// <summary>Selects "everything on screen", so a selectall → copy read-back returns ScreenText
    /// like the real select_all → readSelection round trip.</summary>
    public void SelectAll()
    {
        SelectAllCount += 1;
        Selection = ScreenText;
    }

    public bool Paste()
    {
        if (!Realized) return false;
        PasteCount += 1;
        return true;
    }

    public bool PerformBindingAction(string action)
    {
        if (!Realized) return false;
        BindingActions.Add(action);
        return true;
    }

    public void Teardown() => TeardownCount += 1;

    public void PromoteToPrimaryPane() => PromoteToPrimaryCount += 1;
}
