namespace AgTerm.App;

public sealed class FullscreenController
{
    private readonly Action<bool> _apply;
    private readonly Action<bool> _presentChrome;

    public FullscreenController(Action<bool> apply, Action<bool>? presentChrome = null)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _presentChrome = presentChrome ?? (_ => { });
    }

    public bool IsFullscreen { get; private set; }

    public void Toggle()
    {
        var next = !IsFullscreen;
        _apply(next);
        Commit(next);
    }

    public void Synchronize(bool isFullscreen) => Commit(isFullscreen);

    // the presenter change event can arrive inside _apply, so the chrome is presented on change only
    private void Commit(bool isFullscreen)
    {
        if (IsFullscreen == isFullscreen)
            return;
        IsFullscreen = isFullscreen;
        _presentChrome(isFullscreen);
    }
}
