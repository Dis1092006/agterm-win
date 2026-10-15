using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E.Ported;

/// <summary>
/// Port of agtermUITests/ControlAPIThemeUITests.swift: the light/dark theme control API
/// (theme.set per-slot + theme.list) spoken directly over the control pipe. Request JSON literals
/// are byte-identical to the upstream Swift. The default fixture host bundles NO themes (the theme
/// catalogue is ctor-injected), so the scenario stands up a second host over the same state dir —
/// a "relaunch" — whose available theme names mirror the ones the Swift scenario uses.
/// </summary>
public class ControlApiThemeTests : ControlApiTestCase
{
    [Fact]
    public void DarkOnlySeedsBuiltinLightAndBareResetClearsBothSlots()
    {
        using var host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = StateDir,
            ConfigDir = StateDir,
            PipeName = "agterm-e2e-" + Guid.NewGuid().ToString("N"),
            AvailableThemes = new[] { "agterm", "Dracula", "Builtin Light" },
        });
        host.Start();

        var darkOnly = Send(host, """{"cmd":"theme.set","args":{"dark":"Dracula"}}""");
        Assert.True(darkOnly["ok"]?.GetValue<bool>());
        Assert.Equal("Builtin Light", darkOnly["result"]?["light"]?.GetValue<string>());
        Assert.Equal("Dracula", darkOnly["result"]?["dark"]?.GetValue<string>());
        Assert.True(darkOnly["result"]?["sync"]?.GetValue<bool>());

        var reset = Send(host, """{"cmd":"theme.set"}""");
        Assert.True(reset["ok"]?.GetValue<bool>());
        Assert.False(reset["result"]?["sync"]?.GetValue<bool>());
        Assert.False(reset["result"]!.AsObject().ContainsKey("theme"));
        Assert.False(reset["result"]!.AsObject().ContainsKey("light"));
        Assert.False(reset["result"]!.AsObject().ContainsKey("dark"));
    }

    // AppActions+Palette.swift: currentLightTheme/currentDarkTheme are nil unless following, and
    // a nil optional is omitted, so the slots must not ride a plain single-theme answer.
    [Fact]
    public void LightAndDarkAreAbsentUnlessFollowingSystemAppearance()
    {
        using var host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = StateDir,
            ConfigDir = StateDir,
            PipeName = "agterm-e2e-" + Guid.NewGuid().ToString("N"),
            AvailableThemes = new[] { "agterm", "Dracula", "Builtin Light" },
        });
        host.Start();

        var plain = Send(host, """{"cmd":"theme.set","args":{"name":"agterm"}}""");
        AssertSingleTheme(plain, "agterm");
        AssertSingleTheme(Send(host, """{"cmd":"theme.list"}"""), "agterm");

        var following = Send(host, """{"cmd":"theme.set","args":{"dark":"Dracula"}}""");
        AssertFollowing(following, light: "agterm", dark: "Dracula");
        AssertFollowing(Send(host, """{"cmd":"theme.list"}"""), light: "agterm", dark: "Dracula");

        var cleared = Send(host, """{"cmd":"theme.set","args":{"dark":"none"}}""");
        AssertSingleTheme(cleared, "agterm");
        AssertSingleTheme(Send(host, """{"cmd":"theme.list"}"""), "agterm");
    }

    private static void AssertSingleTheme(JsonObject response, string theme)
    {
        Assert.True(response["ok"]?.GetValue<bool>(), response.ToJsonString());
        var result = response["result"]!.AsObject();
        Assert.Equal(theme, result["theme"]?.GetValue<string>());
        Assert.True(result.ContainsKey("sync"), result.ToJsonString());
        Assert.False(result["sync"]!.GetValue<bool>());
        Assert.False(result.ContainsKey("light"), result.ToJsonString());
        Assert.False(result.ContainsKey("dark"), result.ToJsonString());
    }

    private static void AssertFollowing(JsonObject response, string light, string dark)
    {
        Assert.True(response["ok"]?.GetValue<bool>(), response.ToJsonString());
        var result = response["result"]!.AsObject();
        Assert.False(result.ContainsKey("theme"), result.ToJsonString());
        Assert.True(result["sync"]!.GetValue<bool>());
        Assert.Equal(light, result["light"]?.GetValue<string>());
        Assert.Equal(dark, result["dark"]?.GetValue<string>());
    }

    [Fact]
    public void ThemeSyncWithSystemAppearance()
    {
        // the upstream app ships a bundled theme catalogue and launches with the "agterm" theme
        // applied (the fresh-install current theme the dark-only set seeds the light slot from).
        // Headless both are ctor state: inject the scenario's theme names and seed the light slot.
        using var host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = StateDir,
            ConfigDir = StateDir,
            PipeName = "agterm-e2e-" + Guid.NewGuid().ToString("N"),
            AvailableThemes = new[] { "agterm", "Dracula", "Builtin Light" },
        });
        host.Actions.Theme.Light = "agterm";
        host.Start();

        // a positional name plus --light targets the same slot twice.
        var both = Send(host, """{"cmd":"theme.set","args":{"name":"Dracula","light":"Builtin Light"}}""");
        Assert.False(both["ok"]?.GetValue<bool>(), $"name + --light should fail: {both.ToJsonString()}");

        // an unknown slot name is rejected, like the single form.
        var bad = Send(host, """{"cmd":"theme.set","args":{"light":"NotARealTheme","dark":"agterm"}}""");
        Assert.False(bad["ok"]?.GetValue<bool>(), $"an unknown theme should fail: {bad.ToJsonString()}");

        // setting the dark slot alone starts syncing; the light side seeds from the current theme
        // (the fresh-install agterm default).
        var darkOnly = Send(host, """{"cmd":"theme.set","args":{"dark":"Dracula"}}""");
        Assert.True(darkOnly["ok"]?.GetValue<bool>(), $"theme.set --dark should succeed: {darkOnly.ToJsonString()}");
        var darkResult = darkOnly["result"]!.AsObject();
        Assert.True(darkResult["sync"]?.GetValue<bool>(), "a dark slot means syncing is on");
        Assert.Equal("agterm", darkResult["light"]?.GetValue<string>()); // seeds from the current theme
        Assert.Equal("Dracula", darkResult["dark"]?.GetValue<string>());
        Assert.False(darkResult.ContainsKey("theme"), "no plain theme while syncing");

        // a plain name replaces the light slot and KEEPS the pair.
        var light = Send(host, """{"cmd":"theme.set","args":{"name":"Builtin Light"}}""");
        var lightResult = light["result"]!.AsObject();
        Assert.True(lightResult["sync"]?.GetValue<bool>(), "a plain set keeps the pair");
        Assert.Equal("Builtin Light", lightResult["light"]?.GetValue<string>());
        Assert.Equal("Dracula", lightResult["dark"]?.GetValue<string>()); // the dark side is untouched

        // theme.list reflects the same derived sync state.
        var listed = Send(host, """{"cmd":"theme.list"}""");
        Assert.True(listed["result"]?["sync"]?.GetValue<bool>(), "theme.list reports sync on");

        // --dark none clears the dark slot: syncing off, the light side survives as the plain theme.
        var cleared = Send(host, """{"cmd":"theme.set","args":{"dark":"none"}}""");
        var clearedResult = cleared["result"]!.AsObject();
        Assert.False(clearedResult["sync"]?.GetValue<bool>(), "--dark none turns syncing off");
        Assert.Equal("Builtin Light", clearedResult["theme"]?.GetValue<string>()); // the light side survives

        // the reserved `none` is case-insensitive: re-establish syncing, then clear with `--dark None`.
        var resynced = Send(host, """{"cmd":"theme.set","args":{"dark":"Dracula"}}""");
        Assert.True(resynced["ok"]?.GetValue<bool>(),
            $"re-establishing the dark slot should succeed: {resynced.ToJsonString()}");
        var clearedCaps = Send(host, """{"cmd":"theme.set","args":{"dark":"None"}}""");
        var clearedCapsResult = clearedCaps["result"]!.AsObject();
        Assert.False(clearedCapsResult["sync"]?.GetValue<bool>(),
            "--dark None (any case) also turns syncing off");
    }

    /// <summary>SendCommand against the scenario host (the fixture's SendCommand is bound to the
    /// default Host): one verbatim newline-JSON request line, one parsed response line.</summary>
    private static JsonObject Send(HeadlessHost host, string line)
    {
        using var pipe = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        pipe.Connect(10_000);
        pipe.Write(Encoding.UTF8.GetBytes(line + "\n"));
        pipe.Flush();
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var n = pipe.Read(chunk);
            if (n == 0) break;
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, n);
            if (newline >= 0)
            {
                buffer.Write(chunk, 0, newline);
                break;
            }
            buffer.Write(chunk, 0, n);
        }
        var response = Encoding.UTF8.GetString(buffer.ToArray());
        var node = JsonNode.Parse(response);
        Assert.True(node is JsonObject, $"response should be a JSON object, got: {response}");
        return (JsonObject)node!;
    }
}
