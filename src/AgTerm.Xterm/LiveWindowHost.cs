using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.ControlServer;

namespace AgTerm.Xterm;

internal sealed record LiveWindowSessionEvent(Guid WorkspaceId, Guid SessionId, string Name);

public sealed class LiveWindowHost
{
    private readonly object _gate = new();
    private readonly object _dispatchLifecycleGate = new();
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly PersistenceStore _persistence;
    private ControlDispatcher _dispatcher;
    private IControlDispatchScopeFactory? _dispatchScopeFactory;
    private bool _acceptingDispatch = true;
    private int _acceptedDispatches;
    private TaskCompletionSource<object?>? _dispatchIdle;
    private bool _liveAttached;
    private readonly string _stateDirectory;
    private readonly string _homeDirectory;
    private string _windowName;

    internal LiveWindowHost(
        Guid windowId,
        string windowName,
        string stateDirectory,
        string homeDirectory,
        ControlEventRing ring,
        RecentClosedStore recentClosedStore,
        bool seedIfEmpty,
        bool launchRestore)
    {
        WindowId = windowId;
        _windowName = windowName;
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
            recentClosedStore: recentClosedStore,
            controlEventSink: draft =>
            {
                if (!bootstrapping) ring.Append(draft with { Window = windowTag });
            })
        {
            PendingCloseFinalizationScheduler = action => RunLocked(action),
        };

        var snapshot = _persistence.Load();
        if (snapshot.Workspaces.Length > 0)
        {
            Store.Restore(snapshot, launchRestore);
        }
        else if (seedIfEmpty)
        {
            var workspace = Store.AddWorkspace(Store.DefaultWorkspaceName);
            Store.AddSession(workspace.Id, homeDirectory);
        }
        else
        {
            Store.Restore(snapshot, launchRestore);
        }
        Store.Save();
        bootstrapping = false;

        Actions = CreateActions();
        _dispatcher = new ControlDispatcher(Actions);
    }

    public Guid WindowId { get; }

    public string WindowName
    {
        get
        {
            lock (_gate) return _windowName;
        }
    }

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
        var accepted = false;
        try
        {
            lock (_dispatchLifecycleGate)
            {
                if (!_acceptingDispatch)
                {
                    return new ControlResponse
                    {
                        Ok = false,
                        Error = "window not open — window.select it first",
                    };
                }
                _acceptedDispatches++;
                accepted = true;
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
        catch
        {
            if (accepted)
            {
                CompleteAcceptedDispatch();
                accepted = false;
            }
            throw;
        }
        finally
        {
            _dispatchGate.Release();
        }
        try
        {
            if (scope is null)
                return response;

            using (scope)
            {
                await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
            return response;
        }
        finally
        {
            if (accepted)
                CompleteAcceptedDispatch();
        }
    }

    internal void StopDispatching()
    {
        lock (_dispatchLifecycleGate) _acceptingDispatch = false;
    }

    internal Task WaitForDispatchQuiescenceAsync()
    {
        lock (_dispatchLifecycleGate)
        {
            return _acceptedDispatches == 0
                ? Task.CompletedTask
                : (_dispatchIdle ??= new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void CompleteAcceptedDispatch()
    {
        TaskCompletionSource<object?>? idle = null;
        lock (_dispatchLifecycleGate)
        {
            _acceptedDispatches--;
            if (_acceptedDispatches == 0)
            {
                idle = _dispatchIdle;
                _dispatchIdle = null;
            }
        }
        idle?.TrySetResult(null);
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

    internal LiveWindowSessionEvent[] CaptureLifecycleSessions()
    {
        lock (_gate)
        {
            return Store.Workspaces
                .SelectMany(workspace => workspace.Sessions.Select(session =>
                    new LiveWindowSessionEvent(workspace.Id, session.Id, session.DisplayName)))
                .ToArray();
        }
    }

    internal (int? AutoFollowMs, bool SidebarVisible) CaptureWindowListState()
    {
        lock (_gate)
        {
            var tree = Store.ControlTree();
            return (tree.AutoFollowMs, Store.SidebarVisible);
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

    internal void Rename(string name)
    {
        lock (_gate)
        {
            _windowName = name;
            Actions.WindowRename(WindowId.ToString("D"), name);
        }
    }

    internal void CancelPendingSave()
    {
        lock (_gate) Store.CancelPendingSave();
    }

    private HeadlessControlActions CreateActions() =>
        new(
            Store,
            Ring,
            WindowId,
            _windowName,
            configDirectory: _stateDirectory,
            homeDirectory: _homeDirectory);
}