using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public interface ILiveControlRuntime
{
    Guid WindowId { get; }
    string WindowName { get; }
    string PipeName { get; }
    AppStore Store { get; }
    ControlEventRing Ring { get; }
    HeadlessControlActions Actions { get; }
    void Bootstrap();
    void Start();
    void Stop();
    void RunLocked(Action action);
    void DetachLive();
}

internal sealed class HeadlessControlRuntime(HeadlessHost host) : ILiveControlRuntime
{
    public Guid WindowId => host.WindowId;
    public string WindowName => host.WindowName;
    public string PipeName => host.PipeName;
    public AppStore Store => host.Store;
    public ControlEventRing Ring => host.Ring;
    public HeadlessControlActions Actions => host.Actions;
    public void Bootstrap() => host.Bootstrap();
    public void Start() => host.Start();
    public void Stop() => host.Stop();
    public void RunLocked(Action action) => host.RunLocked(action);
    public void DetachLive() { }
}

internal sealed class WindowControlRuntime(
    LiveWindowHost host,
    string pipeName) : ILiveControlRuntime
{
    public Guid WindowId => host.WindowId;
    public string WindowName => host.WindowName;
    public string PipeName => pipeName;
    public AppStore Store => host.Store;
    public ControlEventRing Ring => host.Ring;
    public HeadlessControlActions Actions => host.Actions;
    public void Bootstrap() => host.BootstrapLive();
    public void Start() { }
    public void Stop() { }
    public void RunLocked(Action action) => host.RunLocked(action);
    public void DetachLive() => host.DetachLive();
}