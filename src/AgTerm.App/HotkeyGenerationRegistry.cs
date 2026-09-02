using AgTerm.Core.Keymap;

namespace AgTerm.App;

public sealed record HotkeyGenerationState(
    long Generation,
    Keymap Keymap,
    HotkeyRouter Router);

public sealed class HotkeyGenerationRegistry
{
    private readonly Dictionary<long, HotkeyGenerationState> _states = new();
    private long _nextGeneration;

    public HotkeyGenerationState Active { get; private set; } =
        new(0, Keymap.Empty, new HotkeyRouter(Keymap.Empty));

    public HotkeyGenerationState Prepare(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var state = new HotkeyGenerationState(
            ++_nextGeneration,
            keymap,
            new HotkeyRouter(keymap));
        _states[state.Generation] = state;
        return state;
    }

    public void Commit(long generation)
    {
        if (!_states.TryGetValue(generation, out var state))
            throw new InvalidOperationException($"Unknown hotkey generation: {generation}");

        Active = state;
        _states.Clear();
        _states[generation] = state;
    }

    public void Reject(long generation)
    {
        if (generation != Active.Generation)
            _states.Remove(generation);
    }

    public HotkeyGenerationState? Resolve(long generation) =>
        generation == Active.Generation
            ? Active
            : _states.GetValueOrDefault(generation);
}
