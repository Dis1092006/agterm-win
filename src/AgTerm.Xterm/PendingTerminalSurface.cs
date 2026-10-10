using System.Threading;
using System.Threading.Tasks;
using AgTerm.ControlServer;

namespace AgTerm.Xterm;

public sealed class PendingTerminalSurface : IHeadlessSurface
{
    private readonly CancellationTokenSource _reservationCts = new();
    private readonly TaskCompletionSource<IHeadlessSurface?> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _torndown;

    public string? PaneToken => null;

    public CancellationToken ReservationToken => _reservationCts.Token;

    public Task<IHeadlessSurface?> Ready => _ready.Task;

    public bool Inject(string text) => false;

    public string? ReadScreenText(bool all, int? lines) => null;

    public string? SelectedText => null;

    public bool IsRealized => false;

    public void SelectAll()
    {
    }

    public bool Paste() => false;

    public bool PerformBindingAction(string action) => false;

    public void PromoteToPrimaryPane()
    {
    }

    public void Teardown()
    {
        if (Interlocked.Exchange(ref _torndown, 1) != 0)
        {
            return;
        }

        _reservationCts.Cancel();
        // Do not dispose here: an unscoped operation may be scheduled but not yet have linked this
        // token. CancellationToken.Register fails after its source is disposed.
        _ready.TrySetResult(null);
    }

    internal void Settle(IHeadlessSurface? surface)
    {
        _ready.TrySetResult(surface);
    }
}