using System.Text.Json;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public sealed class SharedSettingsLiveTests(WebView2Fixture webView)
{
    [Fact]
    public async Task Shared_edits_apply_to_all_live_pane_roles_and_future_panes_without_restarting_shells()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-shared-settings-live-").FullName;
        var hosts = new List<LiveControlHost>();
        var subscriptions = new List<IDisposable>();
        try
        {
            var store = new SettingsStore(directory);
            var coordinator = new SettingsCoordinator(store);
            var pages = new List<XtermPage>();
            for (var index = 0; index < 2; index++)
            {
                var page = await webView.CreatePageAsync();
                pages.Add(page);
                var host = await Task.Run(() => new LiveControlHost(new LiveControlHostOptions
                {
                    StateDir = Path.Combine(directory, $"window-{index}"),
                    HomeDirectory = directory,
                    PipeName = "agterm-shared-live-" + Guid.NewGuid().ToString("N"),
                    DefaultShell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                    AvailableThemes = ThemeCatalog.Default.Names,
                    OpenSurface = (request, cancellationToken) => webView.RunAsync<IHeadlessSurface>(async _ =>
                        await XtermPaneSurface.OpenAsync(page, request.Options, cancellationToken)),
                }));
                hosts.Add(host);
                subscriptions.Add(await coordinator.SubscribeAsync(async (snapshot, kind) =>
                {
                    await host.ApplySettingsAsync(snapshot);
                    var renderer = LiveThemeSettings.Resolve(snapshot.Settings, ThemeCatalog.Default, false,
                        preservePaneFontSizes: kind is SettingsApplyKind.Startup or SettingsApplyKind.Theme).RendererSettings;
                    if (kind is not (SettingsApplyKind.Startup or SettingsApplyKind.Theme))
                        await host.ApplyBaseFontSizeAsync(renderer.FontSize);
                    await webView.RunAsync<object?>(async _ =>
                    {
                        await page.ApplyRendererSettingsAsync(renderer);
                        return null;
                    });
                }));
                host.Start();
            }
            var first = new ControlPipeClient(hosts[0].PipeName);
            var second = new ControlPipeClient(hosts[1].PipeName);
            var marker = "stable-" + Guid.NewGuid().ToString("N");
            Ok(first.Send(new ControlRequest { Cmd = Command.SessionType, Args = new ControlArgs { Text = $"set SETTINGS_PROBE={marker}\n" } }));
            Ok(first.Send(new ControlRequest { Cmd = Command.SessionSplit, Args = new ControlArgs { Mode = "on" } }));
            Ok(first.Send(new ControlRequest { Cmd = Command.SessionScratch, Args = new ControlArgs { Mode = "on" } }));
            Ok(second.Send(new ControlRequest { Cmd = Command.SessionOverlayOpen, Args = new ControlArgs { Command = "cmd.exe /d /q" } }));
            Ok(second.Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "show" } }));
            var before = await CaptureAsync(hosts);
            Assert.Equal(6, before.Count);

            var saved = await coordinator.UpdateAsync(store.ReadStrict(), settings => settings with
            {
                FontFamily = "Consolas", FontSize = 21, Theme = "Dracula", RightClickPaste = false,
                MouseScrollMultiplier = 7, BackgroundOpacity = 0.8,
            });
            Assert.Null(coordinator.State.Error);
            Assert.Equal(21, saved.Settings.FontSize);
            var after = await CaptureAsync(hosts);
            Assert.Equal(before, after);
            for (var index = 0; index < hosts.Count; index++)
            {
                foreach (var surface in await SurfacesAsync(hosts[index]))
                    await AssertRendererAsync(pages[index], surface);
                var theme = hosts[index].Host.Actions.ListThemes().Result!;
                Assert.Equal("Dracula", theme.Theme);
            }
            Ok(second.Send(new ControlRequest { Cmd = Command.SessionNew }));
            var future = await hosts[1].StoreCoordinator.ReadAsync(storeModel =>
                Assert.IsType<XtermPaneSurface>(storeModel.ActiveSession!.Surface));
            await AssertRendererAsync(pages[1], future);

            Ok(first.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Args = new ControlArgs { Pane = "left", Text = "echo %SETTINGS_PROBE%-after\n" },
            }));
            var deadline = Environment.TickCount64 + 30000;
            string text;
            do
            {
                text = Ok(first.Send(new ControlRequest
                {
                    Cmd = Command.SessionText, Args = new ControlArgs { Pane = "left", All = true },
                })).Result!.Text!;
                if (text.Contains(marker + "-after", StringComparison.Ordinal)) break;
                await Task.Delay(50);
            } while (Environment.TickCount64 < deadline);
            Assert.Contains(marker + "-after", text, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
            foreach (var host in hosts) await host.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task AssertRendererAsync(XtermPage page, XtermPaneSurface surface)
    {
        var raw = await webView.RunAsync(_ => page.ExecuteScriptAsync(
            $"agterm.getRendererSettings({JsonSerializer.Serialize(surface.PaneToken)})"));
        using var json = JsonDocument.Parse(raw);
        var settings = json.RootElement;
        Assert.Equal("Consolas", settings.GetProperty("fontFamily").GetString());
        Assert.Equal(21, settings.GetProperty("fontSize").GetDouble());
        Assert.False(settings.GetProperty("rightClickPaste").GetBoolean());
        Assert.Equal(7, settings.GetProperty("scrollSensitivity").GetDouble());
        Assert.Equal(0.8, settings.GetProperty("backgroundOpacity").GetDouble());
    }

    private static async Task<List<XtermPaneSurface>> CaptureAsync(IEnumerable<LiveControlHost> hosts)
    {
        var surfaces = new List<XtermPaneSurface>();
        foreach (var host in hosts) surfaces.AddRange(await SurfacesAsync(host));
        return surfaces;
    }

    private static Task<XtermPaneSurface[]> SurfacesAsync(LiveControlHost host) =>
        host.StoreCoordinator.ReadAsync(store => store.Workspaces.SelectMany(workspace => workspace.Sessions)
            .SelectMany(session => new[] { session.Surface, session.SplitSurface, session.ScratchSurface, session.OverlaySurface })
            .Append(host.Host.Actions.Quick.Surface).OfType<XtermPaneSurface>().ToArray());

    private static ControlResponse Ok(ControlResponse response)
    {
        Assert.True(response.Ok, response.Error);
        return response;
    }
}
