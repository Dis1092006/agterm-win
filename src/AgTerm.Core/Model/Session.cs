using AgTerm.Core.Keymap;

namespace AgTerm.Core.Model;

/// <summary>
/// The host-free surface abstraction a Session's pane slots hold: the Windows host wraps a live
/// terminal (ConPTY + renderer) behind this. PaneToken is the surface's stable spawn token (baked
/// into the shell as AGTERM_PANE_ID) used to resolve pane roles live.
/// </summary>
public interface ITerminalSurface
{
    string? PaneToken { get; }

    /// <summary>Frees the underlying terminal surface and shell. Called when the owning session
    /// (or pane) is closed.</summary>
    void Teardown();

    /// <summary>Reassigns this surface from the split (right) pane role to the primary pane, so its
    /// live pwd/title reports flow to the session's main fields instead of the split fields. Called
    /// when the primary pane's shell exits and the surviving split pane is promoted.</summary>
    void PromoteToPrimaryPane();
}

/// <summary>Optional non-blocking exit-code projection for a terminal process that has already
/// completed. Hosts that cannot report a process status do not implement this interface.</summary>
public interface ICompletedExitCodeSurface
{
    int? CompletedExitCode { get; }
}

/// <summary>
/// One shell session: a sidebar row owning a main pane, an optional split pane, an ephemeral
/// scratch shell, and an ephemeral overlay. Pure model state — surfaces are opaque host handles.
/// Not thread-safe; the host confines it to its model context (upstream is @MainActor).
/// </summary>
public sealed class Session
{
    public Session(string initialCwd, Guid? id = null, string? customName = null)
    {
        Id = id ?? Guid.NewGuid();
        InitialCwd = initialCwd;
        CustomName = customName;
    }

    public Guid Id { get; }

    /// <summary>Manual rename; wins in DisplayName. A blank rename is stored as null.</summary>
    public string? CustomName { get; set; }

    /// <summary>The live working directory from the latest OSC 7 / PWD report. Captured by
    /// ToSnapshot(), but a bare cd does not trigger a save.</summary>
    public string? CurrentCwd { get; set; }

    public string InitialCwd { get; }

    /// <summary>The terminal title from the latest OSC 0/1/2 report. Ephemeral.</summary>
    public string? OscTitle { get; set; }

    /// <summary>The split (right) pane's live cwd/title; null when there is no split pane.</summary>
    public string? SplitCwd { get; set; }
    public string? SplitTitle { get; set; }

    /// <summary>Count of unseen terminal notifications while the session wasn't focused. Ephemeral.</summary>
    public int UnseenCount { get; set; }

    /// <summary>The per-session agent status, driven over the control channel. Ephemeral.</summary>
    public AgentIndicator AgentIndicator { get; set; } = new();

    /// <summary>The most-recent time the agent status was set to a non-idle value — the attention
    /// list's sort key. Ephemeral.</summary>
    public DateTimeOffset? StatusChangedAt { get; set; }

    /// <summary>Whether idle auto-follow has already pulled the user to THIS blocked episode. Ephemeral.</summary>
    public bool AutoFollowConsumed { get; set; }

    /// <summary>Whether this session is in the flagged working-set. Persisted; travels with the session.</summary>
    public bool Flagged { get; set; }

    /// <summary>The main pane's surface, lazily created by the host on first display.</summary>
    public ITerminalSurface? Surface { get; set; }

    /// <summary>Whether the session is currently SHOWN as a side-by-side split.</summary>
    public bool IsSplit { get; set; }

    /// <summary>Whether the session HAS a split pane at all (shown OR hidden/maximized), as opposed
    /// to IsSplit which is only "currently shown". Cleared only when the split is closed.</summary>
    public bool HasSplit { get; set; }

    /// <summary>While split, whether the split (second) pane holds focus. Meaningless when not split.</summary>
    public bool SplitFocused { get; set; }

    /// <summary>The split divider's left-pane fraction (~0.05..0.95); null = even. Persisted.</summary>
    public double? SplitRatio { get; set; }

    /// <summary>The second pane's surface; survives a hide (the shell stays alive).</summary>
    public ITerminalSurface? SplitSurface { get; set; }

    /// <summary>The directory the split pane re-spawns in on restore (persisted splitCwd), or null
    /// for a fresh split which seeds from EffectiveCwd.</summary>
    public string? InitialSplitCwd { get; set; }

    /// <summary>The terminal font size in points, or null for the engine default. Persisted.</summary>
    public double? FontSize { get; set; }

    /// <summary>The session's background watermark, or null for none. Persisted (LOSSY decode).</summary>
    public BackgroundWatermark? BackgroundWatermark { get; set; }

    /// <summary>A command run as the session's process instead of the login shell
    /// (session.new --command). Persisted so a command session re-runs on restore.</summary>
    public string? InitialCommand { get; set; }

    /// <summary>Whether a --command session HOLDS its surface after the command exits
    /// (session.new --command --wait). Meaningful only with InitialCommand. Persisted.</summary>
    public bool CommandWait { get; set; }

    /// <summary>True when this session was rebuilt from a snapshot rather than freshly created;
    /// gates the InitialCommand re-run. Transient.</summary>
    public bool WasRestored { get; set; }

    /// <summary>The main pane's foreground argv captured at the last clean quit, for
    /// restore-running-command. Persisted; null when the pane was at its prompt.</summary>
    public string[]? ForegroundCommand { get; set; }

    /// <summary>The split pane's captured foreground argv, the split analogue of ForegroundCommand.</summary>
    public string[]? SplitForegroundCommand { get; set; }

    /// <summary>The main pane's PERSISTED restore-command override (session.restore). Tri-state:
    /// null = no override (auto-capture), "" = pinned to nothing, "cmd" = run that shell line.
    /// Sticky: fires again after every restart until cleared.</summary>
    public string? RestoreCommand { get; set; }

    /// <summary>The split pane's persisted restore-command override.</summary>
    public string? SplitRestoreCommand { get; set; }

    /// <summary>The main pane's TRANSIENT override payload for THIS launch, copied from the persisted
    /// RestoreCommand by an app-bootstrap restore and consumed by TakePendingRestoreOverride. Never
    /// persisted — a command written over the socket during this run can never execute during this run.</summary>
    public string? PendingRestoreCommand { get; set; }

    /// <summary>The split pane's transient override payload; seeded only when the restored snapshot's
    /// split was SHOWN.</summary>
    public string? PendingSplitRestoreCommand { get; set; }

    /// <summary>Whether an ephemeral overlay terminal is shown on top of this session. Never persisted.</summary>
    public bool OverlayActive { get; set; }

    /// <summary>The overlay's surface, torn down when its program exits or the control channel closes it.</summary>
    public ITerminalSurface? OverlaySurface { get; set; }

    /// <summary>The command the overlay runs as its process.</summary>
    public string? OverlayCommand { get; set; }

    /// <summary>The overlay's working directory, or null to inherit EffectiveCwd.</summary>
    public string? OverlayCwd { get; set; }

    /// <summary>The overlay's own solid background color as #rrggbb, or null for the theme default.</summary>
    public string? OverlayBackgroundColor { get; set; }

    /// <summary>Whether the overlay keeps its surface open after the command exits.</summary>
    public bool OverlayWait { get; set; }

    /// <summary>The overlay program's exit status, recorded on teardown; reset when a new overlay opens.</summary>
    public int? OverlayExitCode { get; set; }

    /// <summary>For a floating overlay, the percent of the pane the panel occupies (1..100); null for
    /// the default full-pane overlay.</summary>
    public int? OverlaySizePercent { get; set; }

    /// <summary>Whether a FULL-coverage overlay is up: active with no size percent.</summary>
    public bool FullOverlayActive => OverlayActive && OverlaySizePercent is null;

    /// <summary>Whether a FLOATING overlay is up: active WITH a size percent.</summary>
    public bool FloatingOverlayActive => OverlayActive && OverlaySizePercent is not null;

    /// <summary>Whether the scratch terminal is shown on top of this session. Never persisted.</summary>
    public bool ScratchActive { get; set; }

    /// <summary>The scratch terminal's surface; alive-but-hidden across hides, freed only on close.</summary>
    public ITerminalSurface? ScratchSurface { get; set; }

    /// <summary>A command run as the scratch's process instead of a login shell. RUN-ONCE: read and
    /// cleared by the scratch surface factory. Transient.</summary>
    public string? ScratchCommand { get; set; }

    /// <summary>Whether the in-terminal search bar is shown over this session's focused pane. Ephemeral.</summary>
    public bool SearchActive { get; set; }

    /// <summary>The current search query. Ephemeral.</summary>
    public string SearchNeedle { get; set; } = "";

    /// <summary>The number of matches for SearchNeedle; null before a query runs. Ephemeral.</summary>
    public int? SearchTotal { get; set; }

    /// <summary>The 1-based index of the selected match; null when none is selected. Ephemeral.</summary>
    public int? SearchSelected { get; set; }

    /// <summary>The surface that owns the open search bar, pinned so the bar keeps driving the SAME
    /// surface even if split focus moves afterwards. Ephemeral.</summary>
    public ITerminalSurface? SearchSurface { get; set; }

    /// <summary>Stable identity of the left pane slot's shell, so an ask anchored to a pane can
    /// tell the pane it covered from the one that replaced it (upstream paneIdentity). Not the
    /// surface's pane token: a session that is not realized has no surface.</summary>
    public Guid PaneIdentity { get; private set; } = Guid.NewGuid();

    private Guid? _splitPaneIdentity;

    /// <summary>The split pane's identity while the session has a split (shown or hidden).</summary>
    public Guid? SplitPaneIdentity => HasSplit ? _splitPaneIdentity ??= Guid.NewGuid() : null;

    /// <summary>The surviving split shell becomes the left pane, and keeps its identity.</summary>
    internal void PromoteSplitPaneIdentity()
    {
        PaneIdentity = _splitPaneIdentity ?? Guid.NewGuid();
        _splitPaneIdentity = null;
    }

    internal void ForgetSplitPaneIdentity() => _splitPaneIdentity = null;

    public OverlayPane? PaneRole(Guid identity)
    {
        if (identity == PaneIdentity) return OverlayPane.Left;
        if (identity == SplitPaneIdentity) return OverlayPane.Right;
        return null;
    }

    /// <summary>Whether the detail area lays this pane out now: both panes while the split is
    /// shown, else the focused one.</summary>
    public bool RendersPane(OverlayPane pane) =>
        IsSplit || pane == (SplitFocused ? OverlayPane.Right : OverlayPane.Left);

    /// <summary>The session's pending terminal question, independent of the overlay slot.
    /// AppStore owns every change, so the registry retains the outcome before the slot clears.</summary>
    public PendingAsk? AskPending { get; internal set; }

    /// <summary>The covered pane's identity; null covers the whole session.</summary>
    public Guid? AskPaneIdentity { get; internal set; }

    /// <summary>The covered pane's current role; null for a session-wide question.</summary>
    public OverlayPane? AskTargetPane => AskPaneIdentity is { } identity ? PaneRole(identity) : null;

    /// <summary>The session's HUD, a passive message panel in the session-wide slot. Its own state
    /// rather than OverlayActive: a HUD runs no process, and OverlayActive makes the live host reserve
    /// a terminal surface. AppStore owns every change. Never persisted.</summary>
    public HudSpec? Hud { get; internal set; }

    /// <summary>The anchor pane's identity; null places the HUD over the whole session.</summary>
    public Guid? HudPaneIdentity { get; internal set; }

    /// <summary>The size the HUD took, measured or forced; null while no HUD is up.</summary>
    public HudPanelSize? HudPanelSize { get; internal set; }

    /// <summary>The width session.overlay.resize forced; it holds through re-measures until the next
    /// open or update.</summary>
    public int? HudForcedWidthPercent { get; internal set; }

    /// <summary>Moves on every open, update and close, so a stale auto-hide and a redraw can tell the
    /// HUD they were made for from the one in the slot now.</summary>
    public int HudRevision { get; internal set; }

    public bool HudActive => Hud is not null;

    /// <summary>The anchor pane's current role; null for a session-wide HUD.</summary>
    public OverlayPane? HudTargetPane => HudPaneIdentity is { } identity ? PaneRole(identity) : null;

    /// <summary>The font the HUD measures and draws at, fixed at open as upstream, so a later session
    /// zoom does not resize the panel; null until some font is known. AppStore owns every change.</summary>
    public double? HudFontSize { get; internal set; }

    /// <summary>The last metrics the page reported for this session, kept with no HUD up so the next
    /// open measures at once. Never persisted.</summary>
    public HudSessionMetrics? HudMetrics { get; internal set; }

    /// <summary>
    /// The sidebar label: a non-blank CustomName wins; otherwise the non-blank terminal title of the
    /// focused pane; otherwise the basename of the focused pane's cwd. Both name sources are trimmed,
    /// so a whitespace-only value falls through. Basename pins: root "/" → "/"; a trailing slash is
    /// ignored ("/a/b/" → "b"); an empty path → "~".
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (TrimmedOrNull(CustomName) is { } custom) return custom;
            if (TrimmedOrNull(FocusedOscTitle) is { } title) return title;
            var path = FocusedCwd;
            return path.Length == 0 ? "~" : LastPathComponent(path);
        }
    }

    /// <summary>
    /// The cwd of the pane currently in focus: the split pane's while it has focus AND its surface
    /// exists (so a promoted survivor can't mask the migrated main-pane cwd), otherwise the primary's
    /// (CurrentCwd, falling back to InitialCwd).
    /// </summary>
    public string FocusedCwd
    {
        get
        {
            if (SplitFocused && SplitSurface is not null && SplitCwd is { } cwd) return cwd;
            return CurrentCwd ?? InitialCwd;
        }
    }

    private string? FocusedOscTitle => SplitFocused && SplitSurface is not null ? SplitTitle : OscTitle;

    /// <summary>
    /// The detail line shown after the workspace name (palette, switcher, title bar): the focused
    /// pane's terminal title when it isn't already the DisplayName (so it ADDS context), else the
    /// focused cwd. Surfaces the remote location of an SSH session instead of the stale local path.
    /// </summary>
    public string SubtitleDetail
    {
        get
        {
            if (TrimmedOrNull(FocusedOscTitle) is { } title && title != DisplayName) return title;
            return FocusedCwd;
        }
    }

    /// <summary>
    /// The session's effective working directory: the live CurrentCwd once a PWD report has arrived,
    /// otherwise InitialCwd. Always the PRIMARY pane's (NOT focus-aware). It seeds a new
    /// split/overlay/quick terminal; custom commands use CwdFor.
    /// </summary>
    public string EffectiveCwd => CurrentCwd ?? InitialCwd;

    /// <summary>The working directory of one pane, for a custom command fired there: the split
    /// pane's live cwd, then its restored one, then the primary's.</summary>
    public string CwdFor(CommandContext.Pane pane) => pane switch
    {
        CommandContext.Pane.Right => SplitCwd ?? InitialSplitCwd ?? EffectiveCwd,
        _ => EffectiveCwd,
    };

    /// <summary>The surface of the pane currently in focus: the split pane while it has focus and
    /// exists, otherwise the primary.</summary>
    public ITerminalSurface? ActiveSurface =>
        SplitFocused && SplitSurface is not null ? SplitSurface : Surface;

    /// <summary>
    /// The session's one addressable pane for the control arms that act on "the session" rather than
    /// a named --pane (session.copy/paste/selectall, font.*): the main pane; the split fallback only
    /// keeps those arms answering should the main slot ever be empty while a split shell lives.
    /// Deliberately NOT focus-aware.
    /// </summary>
    public ITerminalSurface? AddressableSurface => Surface ?? SplitSurface;

    /// <summary>The surface currently on top and owning keyboard focus: an active overlay, else the
    /// scratch, else the active pane.</summary>
    public ITerminalSurface? TopmostSurface
    {
        get
        {
            if (OverlayActive) return OverlaySurface;
            if (ScratchActive) return ScratchSurface;
            return ActiveSurface;
        }
    }

    /// <summary>The pane-or-scratch surface actually ON SCREEN: the scratch when it covers the panes
    /// (and no overlay is up), else the focused pane — what session.text (no --pane) and
    /// session.search resolve through.</summary>
    public ITerminalSurface? OnScreenSurface =>
        ScratchActive && !OverlayActive ? TopmostSurface : ActiveSurface;

    /// <summary>
    /// Resolves a surface's stable spawn token to the pane role of the slot it CURRENTLY occupies —
    /// left for the main slot, right for the split, scratch for the scratch. Live slot occupancy, so
    /// a promoted split survivor resolves left even though its shell was baked with a stale right
    /// role. Null for an empty or unknown token, so the caller falls back to the baked --pane value.
    /// </summary>
    public StatusPane? PaneRole(string token)
    {
        if (token.Length == 0) return null;
        if (Surface?.PaneToken == token) return StatusPane.Left;
        if (SplitSurface?.PaneToken == token) return StatusPane.Right;
        if (ScratchSurface?.PaneToken == token) return StatusPane.Scratch;
        return null;
    }

    /// <summary>
    /// Takes the pane's PENDING restore-command override, clearing it so a second surface for the
    /// same pane this launch gets a plain shell. The PERSISTED overrides are neither read nor written
    /// here — they are sticky and must fire again after the next restart. Scratch always returns null.
    /// </summary>
    public string? TakePendingRestoreOverride(StatusPane pane)
    {
        switch (pane)
        {
            case StatusPane.Left:
                var pending = PendingRestoreCommand;
                PendingRestoreCommand = null;
                return pending;
            case StatusPane.Right:
                var pendingSplit = PendingSplitRestoreCommand;
                PendingSplitRestoreCommand = null;
                return pendingSplit;
            default:
                return null;
        }
    }

    /// <summary>Drops both unconsumed override payloads, leaving the persisted fields alone —
    /// called where a live Session leaves the tree but may come back as the SAME object (the
    /// soft-close grace window).</summary>
    public void ClearPendingRestoreOverrides()
    {
        PendingRestoreCommand = null;
        PendingSplitRestoreCommand = null;
    }

    /// <summary>
    /// The match counter shown in the search bar and returned by session.search: empty before a query
    /// runs, "no matches" at zero, "N matches" while none is selected, "S of N" once one is —
    /// with S clamped to N so a stale index never reads "3 of 2".
    /// </summary>
    public string SearchDisplayText
    {
        get
        {
            if (SearchTotal is not { } total) return "";
            if (total <= 0) return "no matches";
            if (SearchSelected is not { } selected) return $"{total} matches";
            return $"{Math.Min(selected, total)} of {total}";
        }
    }

    /// <summary>Resets all search state to its defaults — called from the pane-teardown/promote paths
    /// so a session whose searched pane was destroyed doesn't keep a stuck, no-op bar.</summary>
    public void ClearSearch()
    {
        SearchActive = false;
        SearchNeedle = "";
        SearchTotal = null;
        SearchSelected = null;
        SearchSurface = null;
    }

    /// <summary>Captures the persisted value of this session: the live cwd (or the initial one before
    /// any PWD report), the split cwd or its restore seed, and CommandWait only when set.</summary>
    public SessionSnapshot ToSnapshot() => new()
    {
        Id = Id,
        CustomName = CustomName,
        Cwd = CurrentCwd ?? InitialCwd,
        IsSplit = IsSplit,
        FontSize = FontSize,
        SplitCwd = SplitCwd ?? InitialSplitCwd,
        SplitRatio = SplitRatio,
        Flagged = Flagged,
        ForegroundCommand = ForegroundCommand,
        SplitForegroundCommand = SplitForegroundCommand,
        InitialCommand = InitialCommand,
        CommandWait = CommandWait ? true : null,
        BackgroundWatermark = BackgroundWatermark,
        RestoreCommand = RestoreCommand,
        SplitRestoreCommand = SplitRestoreCommand,
    };

    /// <summary>The string trimmed of whitespace, or null if the result is empty — the single
    /// normalizer for the rename/DisplayName "blank after trim" rule.</summary>
    public static string? TrimmedOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// The last path component, honoring both separators (the upstream rule is POSIX-only; Windows
    /// paths are accepted equally — see PLATFORM-NOTES): a lone root stays itself, a trailing
    /// separator is ignored.
    /// </summary>
    internal static string LastPathComponent(string path)
    {
        var end = path.Length;
        while (end > 1 && IsSeparator(path[end - 1])) end--;
        if (end == 1 && IsSeparator(path[0])) return path[..1];
        var trimmed = path[..end];
        var idx = trimmed.LastIndexOfAny(Separators);
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;

        static bool IsSeparator(char c) => c is '/' or '\\';
    }

    private static readonly char[] Separators = { '/', '\\' };
}
