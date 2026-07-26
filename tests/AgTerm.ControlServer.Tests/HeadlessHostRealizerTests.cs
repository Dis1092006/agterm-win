using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>
/// The host-level surface realization seam: a host built with a SurfaceRealizer hands it to the
/// store BEFORE bootstrap, so control-driven shows (session.new / session.select / type --select)
/// realize real surfaces, and RunLocked serializes out-of-band mutations (title/cwd/exit reports
/// from surface threads) with the dispatch gate.
/// </summary>
public sealed class HeadlessHostRealizerTests : IDisposable
{
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-realizer-host-").FullName;
    private readonly List<Guid> _realized = new();
    private HeadlessHost? _host;

    private HeadlessHost Host()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
            SurfaceRealizer = session =>
            {
                _realized.Add(session.Id);
                return new FakeSurface { PaneToken = "tok-" + session.Id.ToString("N")[..8] };
            },
        });
        return _host;
    }

    public void Dispose()
    {
        _host?.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Bootstrap_default_session_is_created_shown_and_realized()
    {
        var host = Host();
        var session = Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions));
        Assert.Contains(session.Id, _realized);
        Assert.NotNull(session.Surface);
        Assert.IsType<FakeSurface>(session.Surface);
    }

    [Fact]
    public async Task Control_driven_select_realizes_a_background_session()
    {
        var host = Host();
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var created = client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir, NoSelect = true },
        });
        Assert.True(created.Ok);
        var id = Guid.Parse(created.Result!.Id!);
        Assert.DoesNotContain(id, _realized);

        var selected = client.Send(new ControlRequest { Cmd = Command.SessionSelect, Target = id.ToString() });
        Assert.True(selected.Ok);
        Assert.Contains(id, _realized);
        await Task.CompletedTask;
    }

    [Fact]
    public void Bootstrap_restore_realizes_the_launch_selected_session()
    {
        // seed a snapshot: a host without a realizer, one named session, persisted
        using (var seed = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
        }))
        {
            seed.Store.Save();
        }

        var host = Host(); // restores the snapshot with the realizer installed
        var session = Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions));
        Assert.Equal(session.Id, host.Store.SelectedSessionId);
        Assert.Contains(session.Id, _realized);
        Assert.NotNull(session.Surface);
    }

    [Fact]
    public void RunLocked_serializes_an_out_of_band_mutation()
    {
        var host = Host();
        var session = host.Store.Workspaces.SelectMany(w => w.Sessions).Single();
        host.RunLocked(() => session.OscTitle = "from-surface-thread");
        Assert.Equal("from-surface-thread", session.OscTitle);
    }
}
