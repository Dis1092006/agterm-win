namespace AgTerm.App;

/// <summary>Tells whether a tap on the palette scrim was the click that activated the window. On
/// Windows that click also reaches the content, and Windows reports the activation before the
/// pointer press, so "was the window active at press time" cannot tell the two apart. A pointer
/// activation and a press close together in time are taken as one click, in either order.</summary>
public sealed class ActivationClickFilter
{
    private const long SameClickMs = 500;

    private long? _pointerActivatedAt;
    private long? _pressedAt;

    public void PointerActivated(long nowMs) => _pointerActivatedAt = nowMs;

    public void Pressed(long nowMs) => _pressedAt = nowMs;

    /// <summary>True for the activation click; consumes it, so the next tap counts.</summary>
    public bool ShouldIgnoreTap()
    {
        var ignore = _pressedAt is { } pressed
            && _pointerActivatedAt is { } activated
            && Math.Abs(pressed - activated) <= SameClickMs;
        _pressedAt = null;
        if (ignore)
            _pointerActivatedAt = null;
        return ignore;
    }
}
