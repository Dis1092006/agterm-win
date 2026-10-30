namespace AgTerm.Xterm;

/// <summary>
/// The palette's Edit Keymap row (upstream AppActions.editKeymap and keymapEditOverlaySession):
/// opens the editor command in a 95% floating overlay over the active session and reloads the
/// keymap once that overlay closes. The overlay runs in the keymap's directory, so the command
/// names the file without a path and needs no quotes.
/// </summary>
public sealed class KeymapEditOverlay : IDisposable
{
    private const int SizePercent = 95;

    private readonly ILiveStoreCoordinator _coordinator;
    private readonly Func<Task> _reloadAsync;
    private readonly Action<string, Exception>? _logException;
    private readonly object _lock = new();
    private Guid? _sessionId;
    private int _disposed;

    public KeymapEditOverlay(
        ILiveStoreCoordinator coordinator,
        Func<Task> reloadAsync,
        Action<string, Exception>? logException = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _reloadAsync = reloadAsync ?? throw new ArgumentNullException(nameof(reloadAsync));
        _logException = logException;
        _coordinator.SidebarInvalidated += OnSidebarInvalidated;
    }

    /// <summary>No-op (false) with no active session or when it already has an overlay. The
    /// keymap file itself is not created; the editor creates it on save.</summary>
    public async Task<bool> OpenAsync(string keymapPath, string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keymapPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var directory = Path.GetDirectoryName(Path.GetFullPath(keymapPath))!;
        Directory.CreateDirectory(directory);

        var opened = false;
        await _coordinator.MutateAsync(store =>
        {
            if (store.SelectedSessionId is not { } id ||
                !store.OpenOverlay(id, command, directory, sizePercent: SizePercent))
            {
                return;
            }
            lock (_lock) _sessionId = id;
            opened = true;
        }, cancellationToken).ConfigureAwait(false);
        return opened;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _coordinator.SidebarInvalidated -= OnSidebarInvalidated;
    }

    private void OnSidebarInvalidated()
    {
        Guid id;
        lock (_lock)
        {
            if (_sessionId is not { } tracked) return;
            id = tracked;
        }
        _ = ReloadIfClosedAsync(id);
    }

    private async Task ReloadIfClosedAsync(Guid id)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var open = await _coordinator.ReadAsync(store => store.SessionWithId(id)?.OverlayActive == true)
                .ConfigureAwait(false);
            if (open) return;
            lock (_lock)
            {
                if (_sessionId != id) return;
                _sessionId = null;
            }
            await _reloadAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logException?.Invoke("KeymapEditOverlay reload failed", ex);
        }
    }
}
