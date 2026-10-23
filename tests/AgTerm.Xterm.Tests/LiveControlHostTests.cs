using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;
using Xunit.Abstractions;

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
    private readonly ITestOutputHelper _output;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-host-").FullName;
    private readonly List<LiveControlHost> _hosts = new();

    public LiveControlHostTests(WebView2Fixture webView, ITestOutputHelper output)
    {
        _webView = webView;
        _output = output;
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

    private async Task<(LiveControlHost Host, ControlPipeClient Client)> StartHostAsync()
    {
        // one page per host — the app's one-WebView2-per-window shape; every surface opens a pane on it
        var page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        // Like MainWindow, construct off the calling context so bootstrap can await the UI work.
        var host = await Task.Run(() => new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            Log = _output.WriteLine,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await page.Value;
                return await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken));
            },
        })).ConfigureAwait(false);
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
            var response = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionText,
                Target = target,
                Args = new ControlArgs { All = true },
            }));
            text = response.Result?.Text ?? "";
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"session.text never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    [Fact]
    public async Task Host_bootstrap_does_not_post_to_the_calling_synchronization_context()
    {
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task<(LiveControlHost Host, ControlPipeClient Client)> startup;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            startup = StartHostAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var (_, client) = await startup;
        Assert.Equal(0, context.PostCount);
        WaitForText(client, ">");
    }

    [Theory]
    [InlineData("page")]
    [InlineData("view")]
    [InlineData("run")]
    public async Task Fixture_orchestration_does_not_post_to_the_calling_synchronization_context(
        string operation)
    {
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task work;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            work = operation switch
            {
                "page" => _webView.CreatePageAsync(),
                "view" => _webView.CreateWebViewAsync(),
                "run" => _webView.RunAsync(_ => Task.CompletedTask),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await work;
        Assert.Equal(0, context.PostCount);
    }

    [Fact]
    public void WaitForText_reports_the_wire_error_instead_of_an_empty_buffer()
    {
        var pipeName = "agterm-live-text-error-" + Guid.NewGuid().ToString("N");
        using var server = new ControlPipeServer(pipeName, _ =>
            Task.FromResult<ControlResponse?>(new ControlResponse
            {
                Ok = false,
                Error = "session not realized",
            }));
        server.Start();

        var failure = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            WaitForText(new ControlPipeClient(pipeName), "missing", timeoutMs: 1000));

        Assert.Contains("session not realized", failure.Message);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            base.Post(callback, state);
        }
    }

    [Fact]
    public async Task Type_text_copy_run_against_the_live_default_session()
    {
        var (_, client) = await StartHostAsync();
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
    public async Task Session_new_with_command_realizes_the_argv_child()
    {
        var (_, client) = await StartHostAsync();
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
    public async Task Shell_exit_closes_the_session()
    {
        var (host, client) = await StartHostAsync();
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
    public async Task Osc_title_reaches_the_tree_read_back()
    {
        var (_, client) = await StartHostAsync();
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
    public async Task Sessions_survive_a_host_restart()
    {
        var (host, client) = await StartHostAsync();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir, Name = "keeper" },
        }));
        var id = created.Result!.Id!;
        WaitForText(client, ">", target: id);
        host.Dispose(); // flushes the snapshot

        var (restarted, client2) = await StartHostAsync();
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
    public void A_new_surface_request_carries_the_theme_of_its_workspace()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-workspace-theme-").FullName;
        var captured = new List<SurfaceOpenRequest>();
        try
        {
            var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-workspace-theme-" + Guid.NewGuid().ToString("N"),
                DefaultShell = Cmd,
                AvailableThemes = ["Nord"],
                OpenSurface = (request, _) =>
                {
                    lock (captured) captured.Add(request);
                    return Task.FromResult<IHeadlessSurface>(new FakeSurface());
                },
            });
            using (host)
            {
                host.Start();
                var client = new ControlPipeClient(host.PipeName);
                Ok(client.Send(new ControlRequest
                {
                    Cmd = Command.WorkspaceTheme, Args = new ControlArgs { Name = "Nord" },
                }));
                Ok(client.Send(new ControlRequest { Cmd = Command.SessionNew, Args = new ControlArgs { Cwd = stateDir } }));
                // the new session realizes through OpenSurface; wait for its request before the host stops
                Assert.True(SpinWait.SpinUntil(
                    () => { lock (captured) return captured.Count >= 2; },
                    TimeSpan.FromSeconds(10)));
            }

            lock (captured)
            {
                Assert.Null(captured[0].Options.ThemeName);
                Assert.Equal("Nord", captured[^1].Options.ThemeName);
            }
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
