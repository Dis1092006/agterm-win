namespace AgTerm.App;

public sealed class PaletteAutoFollowLease
{
    public bool IsHeld { get; private set; }

    public async Task AcquireAsync(Func<Task> suppress)
    {
        ArgumentNullException.ThrowIfNull(suppress);
        if (IsHeld)
            return;

        await suppress();
        IsHeld = true;
    }

    public async Task ReleaseAsync(Func<Task> resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        if (!IsHeld)
            return;

        await resume();
        IsHeld = false;
    }
}
