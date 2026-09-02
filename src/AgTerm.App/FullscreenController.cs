namespace AgTerm.App;

public sealed class FullscreenController
{
    private readonly Action<bool> _apply;

    public FullscreenController(Action<bool> apply)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public bool IsFullscreen { get; private set; }

    public void Toggle()
    {
        var next = !IsFullscreen;
        _apply(next);
        IsFullscreen = next;
    }

    public void Synchronize(bool isFullscreen)
    {
        IsFullscreen = isFullscreen;
    }
}