using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.ControlServer;

namespace AgTerm.Xterm;

public sealed class LiveWindowHost
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly PersistenceStore _persistence;
    private ControlDispatcher _dispatcher;
    private IControlDispatchScopeFactory? _dispatchScopeFactory;
    private int _acceptingDispatch = 1;
    private bool _liveAttached;
    private readonly string _stateDirectory;
    private readonly string _homeDirectory;

    internal LiveWindowHost(
        Guid windowId,
        string windowName,
        string stateDirectory,
        string homeDirectory,
        ControlEventRing ring,
        bool seedIfEmpty)
    {
        WindowId = windowId;
        WindowName = windowName;
        Ring = ring;
        _stateDirectory = stateDirectory;
        _homeDirectory = homeDirectory;
        _persistence = new PersistenceStore(
            Path.Combine(stateDirectory, "windows"),
            $"{windowId:D}.json");
        var windowTag = windowId.ToString("D").ToUpperInvariant();
        var bootstrapping = true;
        Store = new AppStore(
            persistence: _persistence,
            controlEventSink: draft =>
            {
                if (!bootstrapping) ring.Append(draft with { Window = windowTag });
            });

        var snapshot = _persistence.Load();
        if (snapshot.Workspaces.Length > 0)
        {
            Store.Restore(snapshot, launchRestore: true);
        }
        else if (seedIfEmpty)
        {
            var workspace = Store.AddWorkspace(Store.DefaultWorkspaceName);
            Store.AddSession(workspace.Id, homeDirectory);
        }
        else
        {
            Store.Restore(snapshot, launchRestore: true);
        }
        Store.Save();
        bootstrapping = false;

        Actions = CreateActions();
        _dispatcher = new ControlDispatcher(Actions);
    }

    public Guid WindowId { get; }

    public string WindowName { get; }

    public ControlEventRing Ring { get; }

    public AppStore Store { get; }

    public HeadlessControlActions Actions { get; private set; }

    internal async Task<ControlResponse?> DispatchAsync(
        ControlRequest request,
        CancellationToken cancellationToken = default)
    {
        IControlDispatchScope? scope = null;
        ControlResponse? response;
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _acceptingDispatch) == 0)
            {
                return new ControlResponse
                {
                    Ok = false,
                    Error = "window not open — window.select it first",
                };
            }

            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scope = _dispatchScopeFactory?.Begin();
                try
                {
                    response = _dispatcher.Dispatch(request).GetAwaiter().GetResult();
                }
                catch
                {
                    scope?.Dispose();
                    throw;
                }
            }
        }
        finally
        {
            _dispatchGate.Release();
        }
        if (scope is null)
            return response;

        using (scope)
        {
            await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    internal void StopDispatching()
    {
        Interlocked.Exchange(ref _acceptingDispatch, 0);
    }

    internal async Task WaitForDispatchQuiescenceAsync()
    {
        await _dispatchGate.WaitAsync().ConfigureAwait(false);
        _dispatchGate.Release();
    }

    internal void RunLocked(Action action)
    {
        lock (_gate) action();
    }

    internal Guid[] CaptureSessionIds()
    {
        lock (_gate)
        {
            return Store.Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .Select(session => session.Id)
                .ToArray();
        }
    }

    internal Guid[] CaptureWorkspaceIds()
    {
        lock (_gate)
        {
            return Store.Workspaces.Select(workspace => workspace.Id).ToArray();
        }
    }

    internal void AttachLive(
        HeadlessControlActions actions,
        Func<Session, ITerminalSurface?> surfaceRealizer,
        IControlDispatchScopeFactory dispatchScopeFactory)
    {
        lock (_gate)
        {
            if (_liveAttached)
                throw new InvalidOperationException($"window is already attached: {WindowId:D}");

            Actions = actions;
            _dispatcher = new ControlDispatcher(actions);
            _dispatchScopeFactory = dispatchScopeFactory;
            Store.SurfaceRealizer = surfaceRealizer;
            Store.EagerSurfaceRealization = true;
            _liveAttached = true;
        }
    }

    internal void BootstrapLive()
    {
        lock (_gate)
        {
            if (!_liveAttached)
                throw new InvalidOperationException($"window is not attached: {WindowId:D}");
            Store.RealizeAllSessions();
        }
    }

    internal void DetachLive()
    {
        lock (_gate)
        {
            if (!_liveAttached)
                return;

            Store.SurfaceRealizer = null;
            Store.EagerSurfaceRealization = false;
            _dispatchScopeFactory = null;
            Actions = CreateActions();
            _dispatcher = new ControlDispatcher(Actions);
            _liveAttached = false;
        }
    }

    internal void Save()
    {
        lock (_gate) Store.Save();
    }

    internal void ResetSessionFontSizes()
    {
        lock (_gate) Store.ResetSessionFontSizes();
    }

    private HeadlessControlActions CreateActions() =>
        new(
            Store,
            Ring,
            WindowId,
            WindowName,
            configDirectory: _stateDirectory,
            homeDirectory: _homeDirectory);
}