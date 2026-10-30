using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The palette's Edit Keymap row (upstream AppActions.editKeymap + WindowContentView's
/// overlay-close onChange): opens the editor in a 95% floating overlay over the active session,
/// in the keymap's directory, and reloads the keymap once that overlay closes.
/// </summary>
public sealed class KeymapEditOverlayTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-kme-").FullName;
    private readonly HeadlessHost _host;
    private readonly LiveStoreCoordinator _coordinator;
    private readonly KeymapEditOverlay _overlay;
    private int _reloads;

    public KeymapEditOverlayTests()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-kme-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        _coordinator = new LiveStoreCoordinator(
            _host,
            isUiThread: () => false,
            scheduler: (work, ct) => { ct.ThrowIfCancellationRequested(); work(); return Task.CompletedTask; });
        _overlay = new KeymapEditOverlay(_coordinator, () =>
        {
            _reloads++;
            return Task.CompletedTask;
        });
    }

    public void Dispose()
    {
        _overlay.Dispose();
        _coordinator.Dispose();
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string KeymapPath => Path.Combine(_stateDir, "config", "keymap.conf");

    private Session Active => _host.Store.ActiveSession!;

    [Fact]
    public async Task Open_runs_the_command_in_the_keymap_directory_as_a_95_percent_overlay()
    {
        var opened = await _overlay.OpenAsync(KeymapPath, "edit.exe keymap.conf");

        Assert.True(opened);
        Assert.True(Active.OverlayActive);
        Assert.Equal("edit.exe keymap.conf", Active.OverlayCommand);
        Assert.Equal(Path.GetDirectoryName(KeymapPath), Active.OverlayCwd);
        Assert.Equal(95, Active.OverlaySizePercent);
        Assert.False(Active.OverlayWait);
        Assert.True(Directory.Exists(Path.GetDirectoryName(KeymapPath)));
        Assert.False(File.Exists(KeymapPath));
        Assert.Equal(0, _reloads);
    }

    [Fact]
    public async Task Closing_the_editor_overlay_reloads_the_keymap_once()
    {
        await _overlay.OpenAsync(KeymapPath, "edit.exe keymap.conf");
        var id = Active.Id;

        await _coordinator.MutateAsync(store => store.CloseOverlay(id));
        await _coordinator.MutateAsync(store => store.NoteUserActivity());
        await _coordinator.MutateAsync(store => store.SetFlag(true, id));

        Assert.False(Active.OverlayActive);
        Assert.Equal(1, _reloads);
    }

    [Fact]
    public async Task Unrelated_overlays_do_not_reload_the_keymap()
    {
        var id = Active.Id;
        await _coordinator.MutateAsync(store => store.OpenOverlay(id, "lazygit"));
        await _coordinator.MutateAsync(store => store.CloseOverlay(id));

        Assert.Equal(0, _reloads);
    }

    [Fact]
    public async Task Open_does_nothing_when_the_active_session_already_has_an_overlay()
    {
        var id = Active.Id;
        await _coordinator.MutateAsync(store => store.OpenOverlay(id, "lazygit"));

        var opened = await _overlay.OpenAsync(KeymapPath, "edit.exe keymap.conf");
        await _coordinator.MutateAsync(store => store.CloseOverlay(id));

        Assert.False(opened);
        Assert.Equal(0, _reloads);
    }

    [Fact]
    public async Task Open_does_nothing_without_an_active_session()
    {
        await _coordinator.MutateAsync(store => store.SelectedSessionId = null);

        var opened = await _overlay.OpenAsync(KeymapPath, "edit.exe keymap.conf");

        Assert.False(opened);
        Assert.All(_host.Store.Workspaces.SelectMany(w => w.Sessions), s => Assert.False(s.OverlayActive));
    }
}
