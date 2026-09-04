using System.Collections.ObjectModel;
using AgTerm.Core.Keymap;

namespace AgTerm.App;

public sealed class WindowCommandHandler
{
    public WindowCommandHandler(Func<bool> canExecute, Func<Task> executeAsync)
    {
        CanExecute = canExecute ?? throw new ArgumentNullException(nameof(canExecute));
        ExecuteAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
    }

    public Func<bool> CanExecute { get; }

    public Func<Task> ExecuteAsync { get; }
}

public readonly record struct WindowCommandState(bool Implemented, bool CanExecute);

public sealed class WindowCommandRouter
{
    public static IReadOnlyDictionary<BuiltinAction, bool> ActionImplementations { get; } =
        new ReadOnlyDictionary<BuiltinAction, bool>(new Dictionary<BuiltinAction, bool>
        {
            [BuiltinAction.NewWindow] = true,
            [BuiltinAction.RenameWindow] = true,
            [BuiltinAction.DeleteWindow] = true,
            [BuiltinAction.NewWorkspace] = true,
            [BuiltinAction.RenameWorkspace] = true,
            [BuiltinAction.DeleteWorkspace] = true,
            [BuiltinAction.NewSession] = true,
            [BuiltinAction.OpenDirectory] = true,
            [BuiltinAction.RenameSession] = true,
            [BuiltinAction.DuplicateSession] = true,
            [BuiltinAction.CloseSession] = true,
            [BuiltinAction.ReopenRecent] = true,
            [BuiltinAction.UndoClose] = true,
            [BuiltinAction.ClearStatus] = true,
            [BuiltinAction.IncreaseFontSize] = true,
            [BuiltinAction.DecreaseFontSize] = true,
            [BuiltinAction.ResetFontSize] = true,
            [BuiltinAction.ToggleSplit] = true,
            [BuiltinAction.ToggleScratch] = true,
            [BuiltinAction.ToggleTerminalZoom] = true,
            [BuiltinAction.ToggleSearch] = true,
            [BuiltinAction.ToggleSidebar] = true,
            [BuiltinAction.SelectTheme] = true,
            [BuiltinAction.ToggleFullscreen] = true,
            [BuiltinAction.ToggleFlaggedView] = true,
            [BuiltinAction.ToggleFlag] = true,
            [BuiltinAction.FocusWorkspace] = true,
            [BuiltinAction.FocusLeftPane] = true,
            [BuiltinAction.FocusRightPane] = true,
            [BuiltinAction.PreviousSession] = true,
            [BuiltinAction.NextSession] = true,
            [BuiltinAction.PreviousAttentionSession] = true,
            [BuiltinAction.NextAttentionSession] = true,
            [BuiltinAction.FirstSession] = true,
            [BuiltinAction.LastSession] = true,
            [BuiltinAction.QuickTerminal] = true,
            [BuiltinAction.SessionPalette] = true,
            [BuiltinAction.CommandPalette] = true,
            [BuiltinAction.CustomCommandPalette] = true,
            [BuiltinAction.ShowAttention] = true,
            [BuiltinAction.Dashboard] = true,
        });

    private static readonly HashSet<BuiltinAction> ImplementedActions = ActionImplementations
        .Where(pair => pair.Value)
        .Select(pair => pair.Key)
        .ToHashSet();

    public static IReadOnlyList<PhysicalChord> ImplementedInterceptChords(
        HotkeyRouter hotkeys,
        Func<BuiltinAction, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        var result = new List<PhysicalChord>();
        var seen = new HashSet<PhysicalChord>();

        foreach (var chord in hotkeys.InterceptChords)
        {
            var winner = hotkeys.Handle(chord);
            if (winner is null)
            {
                continue;
            }

            if (ActionImplementations.TryGetValue(winner.Value, out var implemented) &&
                implemented &&
                (canExecute?.Invoke(winner.Value) ?? true) &&
                seen.Add(chord))
            {
                result.Add(chord);
            }
        }

        return result;
    }

    public static IReadOnlyList<KeymapDiagnostic> EffectiveWindowsChordDiagnostics(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);

        var grouped = new Dictionary<PhysicalChord, List<BuiltinAction>>();
        var physicalOrder = new List<PhysicalChord>();

        foreach (var action in BuiltinActions.All)
        {
            var active = keymap.EquivalentFor(action);
            if (active is null)
            {
                continue;
            }

            var physical = WindowsChordDecoder.Effective(active.Value);
            if (!grouped.TryGetValue(physical, out var actions))
            {
                actions = new List<BuiltinAction>();
                grouped[physical] = actions;
                physicalOrder.Add(physical);
            }

            actions.Add(action);
        }

        var hotkeys = new HotkeyRouter(keymap);
        var diagnostics = new List<KeymapDiagnostic>();

        foreach (var physical in physicalOrder)
        {
            var actions = grouped[physical];
            if (actions.Count <= 1)
            {
                continue;
            }

            var winner = hotkeys.Handle(physical);
            if (winner is null)
            {
                continue;
            }

            var winnerAction = winner.Value;
            var physicalText = FormatPhysicalChord(physical);
            var winnerWire = winnerAction.WireName();

            foreach (var action in actions)
            {
                if (action == winnerAction)
                {
                    continue;
                }

                diagnostics.Add(new KeymapDiagnostic(
                    0,
                    $"effective Windows chord '{physicalText}' resolves to built-in '{winnerWire}'; built-in '{action.WireName()}' is shadowed"));
            }
        }

        return diagnostics;
    }

    private static string FormatPhysicalChord(PhysicalChord physical)
    {
        var parts = new List<string>(4);
        if (physical.Ctrl)
        {
            parts.Add("ctrl");
        }

        if (physical.Alt)
        {
            parts.Add("alt");
        }

        if (physical.Shift)
        {
            parts.Add("shift");
        }

        parts.Add(physical.Key.ToLowerInvariant());
        return string.Join("+", parts);
    }

    private readonly IReadOnlyDictionary<BuiltinAction, WindowCommandHandler> handlers;

    public WindowCommandRouter(IReadOnlyDictionary<BuiltinAction, WindowCommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var copy = new Dictionary<BuiltinAction, WindowCommandHandler>(handlers.Count);
        foreach (var pair in handlers)
        {
            if (pair.Value is null)
            {
                throw new ArgumentException("Handlers cannot contain null values.", nameof(handlers));
            }

            copy[pair.Key] = pair.Value;
        }

        if (!copy.Keys.ToHashSet().SetEquals(ImplementedActions))
        {
            throw new ArgumentException("Handlers must contain exactly the implemented builtin actions.", nameof(handlers));
        }

        this.handlers = new ReadOnlyDictionary<BuiltinAction, WindowCommandHandler>(copy);
    }

    public WindowCommandState GetState(BuiltinAction action)
    {
        if (!ActionImplementations.TryGetValue(action, out var implemented) || !implemented)
        {
            return new WindowCommandState(Implemented: false, CanExecute: false);
        }

        var handler = handlers[action];
        return new WindowCommandState(Implemented: true, CanExecute: handler.CanExecute());
    }

    public bool CanExecute(BuiltinAction action)
    {
        return GetState(action).CanExecute;
    }

    public async Task<bool> ExecuteAsync(BuiltinAction action)
    {
        if (!CanExecute(action))
        {
            return false;
        }

        await handlers[action].ExecuteAsync().ConfigureAwait(false);
        return true;
    }

    public Task<bool> ExecuteChordAsync(HotkeyRouter hotkeys, PhysicalChord physical)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        var action = hotkeys.Handle(physical);
        if (action is null)
        {
            return Task.FromResult(false);
        }

        return ExecuteAsync(action.Value);
    }
}
