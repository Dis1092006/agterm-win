using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The live control host end-to-end: real requests over a REAL named pipe → dispatcher → live
/// AppStore whose sessions realize REAL surfaces (ConPTY cmd.exe ↔ xterm.js in a hidden WebView2).
/// This is the app's control plane minus WinUI. cmd.exe drives; each test gets an isolated state
/// dir + pipe name.
/// </summary>
[Collection("webview2")]
public sealed class LiveControlHostTests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-host-").FullName;
    private readonly List<LiveControlHost> _hosts = new();

    public LiveControlHostTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (LiveControlHost Host, ControlPipeClient Client) StartHost()
    {
        // one page per host — the app's one-WebView2-per-window shape; every surface opens a pane on it
        var page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await page.Value;
                return await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken));
            },
        });
        _hosts.Add(host);
        host.Start();
        return (host, new ControlPipeClient(host.PipeName));
    }

    private static ControlResponse Ok(ControlResponse response)
    {
        Assert.True(response.Ok, $"expected ok, got error: {response.Error}");
        return response;
    }

    private static string WaitForText(ControlPipeClient client, string contains,
                                      string? target = null, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            var response = client.Send(new ControlRequest
            {
                Cmd = Command.SessionText,
                Target = target,
                Args = new ControlArgs { All = true },
            });
            text = response.Result?.Text ?? "";
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"session.text never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    [Fact]
    public void Type_text_copy_run_against_the_live_default_session()
    {
        var (_, client) = StartHost();
        WaitForText(client, ">"); // the default session realized and its cmd prompt painted

        Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Args = new ControlArgs { Text = "echo live-ctl-marker\n" },
        }));
        WaitForText(client, "live-ctl-marker");

        Ok(client.Send(new ControlRequest { Cmd = Command.SessionSelectAll }));
        var copied = Ok(client.Send(new ControlRequest { Cmd = Command.SessionCopy }));
        Assert.Contains("live-ctl-marker", copied.Result!.Text);
    }

    [Fact]
    public void Session_new_with_command_realizes_the_argv_child()
    {
        var (_, client) = StartHost();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            // the shell path rides UNQUOTED — backslashes in Windows paths must survive tokenization
            Args = new ControlArgs { Cwd = _stateDir, Command = $"{Cmd} /k echo cmd-mode-live-ok" },
        }));
        var id = created.Result!.Id!;
        WaitForText(client, "cmd-mode-live-ok", target: id);
    }

    [Fact]
    public void Shell_exit_closes_the_session()
    {
        var (host, client) = StartHost();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir },
        }));
        var id = Guid.Parse(created.Result!.Id!);
        WaitForText(client, ">", target: id.ToString());

        Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id.ToString(),
            Args = new ControlArgs { Text = "exit\n" },
        }));
        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            var gone = false;
            host.Host.RunLocked(() => gone = host.Host.Store.SessionWithId(id) is null);
            if (gone) return;
            Thread.Sleep(100);
        }
        Assert.Fail("the exited session never left the store");
    }

    [Fact]
    public void Osc_title_reaches_the_tree_read_back()
    {
        var (_, client) = StartHost();
        WaitForText(client, ">");
        Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Args = new ControlArgs { Text = "title live-tree-title\n" },
        }));
        var deadline = Environment.TickCount64 + 30000;
        string? seen = null;
        while (Environment.TickCount64 < deadline)
        {
            var tree = Ok(client.Send(new ControlRequest { Cmd = Command.Tree }));
            seen = tree.Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).First().Title;
            if (seen is not null && seen.Contains("live-tree-title")) return;
            Thread.Sleep(100);
        }
        Assert.Fail($"tree title never carried the marker; last: {seen}");
    }

    [Fact]
    public void Sessions_survive_a_host_restart()
    {
        var (host, client) = StartHost();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir, Name = "keeper" },
        }));
        var id = created.Result!.Id!;
        WaitForText(client, ">", target: id);
        host.Dispose(); // flushes the snapshot

        var (restarted, client2) = StartHost();
        Session? restored = null;
        restarted.Host.RunLocked(() =>
            restored = restarted.Host.Store.SessionWithId(Guid.Parse(id)));
        Assert.NotNull(restored);
        Assert.Equal("keeper", restored!.CustomName);
        Assert.Equal(_stateDir, restored.InitialCwd);
        // the launch-selected session must realize at bootstrap (upstream: the window shows it) —
        // a live shell answers session.text right after the restart
        WaitForText(client2, ">");
    }

    [Fact]
    public void Configured_base_font_size_reaches_new_surface_options()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-fontsize-").FullName;
        var capturedSizes = new List<double>();
        try
        {
            var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-fontsize-" + Guid.NewGuid().ToString("N"),
                DefaultShell = Cmd,
                BaseFontSize = 18,
                OpenSurface = (request, _) =>
                {
                    lock (capturedSizes) capturedSizes.Add(request.Options.BaseFontSize);
                    return Task.FromResult<IHeadlessSurface>(new FakeSurface());
                },
            });
            using (host)
            {
                host.Start();
            }
            Assert.Contains(18.0, capturedSizes);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Config_reload_invokes_live_host_callback_and_preserves_wire_count()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-cfgreload-").FullName;
        var calls = 0;
        try
        {
            var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-cfgreload-" + Guid.NewGuid().ToString("N"),
                DefaultShell = Cmd,
                ConfigReload = () => { calls++; return 7; },
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
            });
            using (host)
            {
                host.Start();
                var client = new ControlPipeClient(host.PipeName);
                var response = client.Send(new ControlRequest { Cmd = Command.ConfigReload });
                Assert.True(response.Ok, $"expected ok, got error: {response.Error}");
                Assert.Equal(7, response.Result?.Count);
                Assert.Equal(1, calls);
            }
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ApplyBaseFontSizeAsync_clears_session_overrides_and_updates_future_reservations()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-fontreload-").FullName;
        var reservations = new List<(Guid SessionId, double BaseFontSize, FakeSurface Fake)>();
        var bootstrapOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-fontreload-" + Guid.NewGuid().ToString("N"),
                DefaultShell = Cmd,
                BaseFontSize = 14,
                OpenSurface = (request, _) =>
                {
                    var fake = new FakeSurface();
                    lock (reservations)
                    {
                        reservations.Add((request.SessionId!.Value, request.Options.BaseFontSize, fake));
                        if (reservations.Count == 1) bootstrapOpened.TrySetResult();
                        else if (reservations.Count == 2) secondOpened.TrySetResult();
                    }
                    return Task.FromResult<IHeadlessSurface>(fake);
                },
            });
            using (host)
            {
                host.Start();
                await bootstrapOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));

                Guid activeId;
                FakeSurface bootstrapFake;
                lock (reservations)
                {
                    Assert.Equal(14.0, reservations[0].BaseFontSize);
                    activeId = reservations[0].SessionId;
                    bootstrapFake = reservations[0].Fake;
                }

                await host.StoreCoordinator.MutateAsync(store => store.SetFontSize(activeId, 20));

                // ApplyBaseFontSizeAsync does not exist yet — compile error is the RED signal
                await host.ApplyBaseFontSizeAsync(18);

                double? sizeAfter = await host.StoreCoordinator.ReadAsync(
                    store => store.SessionWithId(activeId)?.FontSize);
                Assert.Null(sizeAfter);

                var client = new ControlPipeClient(host.PipeName);
                client.Send(new ControlRequest
                {
                    Cmd = Command.SessionNew,
                    Args = new ControlArgs { Cwd = stateDir },
                });
                await secondOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));

                double secondBase;
                lock (reservations) secondBase = reservations[1].BaseFontSize;
                Assert.Equal(18.0, secondBase);

                Assert.Equal(0, bootstrapFake.TeardownCount);
            }
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ApplyBaseFontSizeAsync_same_base_still_clears_session_override()
    {
        var surfaces = new List<FakeSurface>();
        using var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-same-font-" + Guid.NewGuid().ToString("N"),
            BaseFontSize = 14,
            OpenSurface = (_, _) =>
            {
                var surface = new FakeSurface();
                surfaces.Add(surface);
                return Task.FromResult<IHeadlessSurface>(surface);
            },
        });

        var selected = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        await host.StoreCoordinator.MutateAsync(store => store.SetFontSize(selected, 20));

        await host.ApplyBaseFontSizeAsync(14);

        var overrideAfterReload = await host.StoreCoordinator.ReadAsync(
            store => store.SessionWithId(selected)!.FontSize);
        Assert.Null(overrideAfterReload);
        Assert.Single(surfaces);
        Assert.Equal(0, surfaces[0].TeardownCount);
    }
}
