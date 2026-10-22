using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;

namespace AgTerm.E2E;

public class PublishedCliTests : ControlApiTestCase
{
    [PublishedCliFact]
    public void PublishedExecutableIsNativeRatherThanAnApphostOrBundle()
    {
        using var stream = File.OpenRead(Executable);
        using var pe = new PEReader(stream);

        Assert.Null(pe.PEHeaders.CorHeader);
        Assert.False(pe.HasMetadata);
        // The SDK apphost's bundle_marker.h signature is present even before a single-file bundle is attached.
        var bundleMarker = Convert.FromHexString("8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE");
        Assert.Equal(-1, File.ReadAllBytes(Executable).AsSpan().IndexOf(bundleMarker));
    }

    [PublishedCliFact]
    public async Task PublishedExecutablePreservesHelpVersionAndUsage()
    {
        foreach (var arguments in new[] { new[] { "--help" }, ["tree", "--help"], ["session", "status", "--help"] })
        {
            var result = await RunPublished(arguments);
            Assert.Equal(0, result.Exit);
            Assert.Contains("Usage:", result.Out);
            Assert.Equal("", result.Error);
        }

        var version = await RunPublished(["--version"]);
        Assert.Equal(0, version.Exit);
        Assert.Equal(typeof(AgTerm.Cli.Agtermctl).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
            version.Out.Trim());
        Assert.Equal("", version.Error);

        foreach (var arguments in new[] { new[] { "--bogus" }, ["workspace", "rename"], ["session", "status", "invalid"], ["session", "resize", "--split-ratio", "nan"] })
            Assert.Equal(RunCtl(arguments), await RunPublished(arguments));
    }

    [PublishedCliFact]
    public async Task PublishedExecutablePreservesPipeResultsAndErrors()
    {
        foreach (var arguments in new[]
                 {
                     new[] { "tree", "--json" }, ["window", "list", "--json"], ["theme", "list", "--json"],
                     ["session", "status", "active", "--blink"], ["session", "status", "completed", "--json"],
                     ["session", "select", "--target", "zzzz"], ["session", "select", "--target", "zzzz", "--json"],
                 })
            Assert.Equal(RunCtl(arguments), await RunPublished(arguments));
    }

    [PublishedCliFact]
    public async Task PublishedExecutableDecodesPickerInputAndPrintsBareResults()
    {
        var opened = await RunPublished(["pick", "--no-block"], """[{"id":"a","label":"<A>","subtitle":"\u0410"}]""");
        Assert.Equal(0, opened.Exit);
        Assert.Equal("", opened.Error);
        var id = JsonNode.Parse(opened.Out)!["id"]!.GetValue<string>();
        Host.Actions.Pick.Resolve(id, AgTerm.Core.Protocol.ControlPickResult.Picked("a", "<A>", 0));

        var result = await RunPublished(["pick", "result", id]);
        Assert.Equal(0, result.Exit);
        Assert.Equal("{\"result\":\"picked\",\"id\":\"a\",\"label\":\"<A>\",\"index\":0}" + Environment.NewLine, result.Out);
        Assert.Equal("", result.Error);

        var invalid = await RunPublished(["pick", "--no-block"], "[{}]");
        Assert.Equal(1, invalid.Exit);
        Assert.Equal("error: stdin must be UTF-8 text or a JSON item array" + Environment.NewLine, invalid.Error);
        Assert.Equal("", invalid.Out);
    }

    [PublishedCliFact]
    public async Task PublishedExecutablePreservesAskResultsAndExitCodes()
    {
        var outcomes = new[]
        {
            (Result: AgTerm.Core.Protocol.ControlAskResult.Answered("yes", "Yes", 0), Exit: 0),
            (Result: AgTerm.Core.Protocol.ControlAskResult.Cancelled(), Exit: 2),
            (Result: AgTerm.Core.Protocol.ControlAskResult.Escaped(), Exit: 3),
        };
        foreach (var outcome in outcomes)
        {
            var opened = await RunPublished(["ask", "Deploy?", "--button", "yes=Yes", "--style", "gui", "--no-block"]);
            Assert.Equal(0, opened.Exit);
            var id = JsonNode.Parse(opened.Out)!["id"]!.GetValue<string>();
            Host.Actions.Pick.ResolveAsk(id, outcome.Result);

            var result = await RunPublished(["ask", "result", id]);
            Assert.Equal(outcome.Exit, result.Exit);
            Assert.Equal(AgTerm.Core.Protocol.ControlJson.Serialize(outcome.Result) + Environment.NewLine, result.Out);
            Assert.Equal("", result.Error);
        }
    }

    [PublishedCliFact]
    public async Task PublishedExecutablePreservesEventSerialization()
    {
        var request = SendOk("""{"cmd":"events.read"}""")["events"]!.AsObject();
        var run = request["run"]!.GetValue<string>();
        var after = request["next"]!.GetValue<ulong>();
        Assert.Equal(0, (await RunPublished(["session", "status", "blocked"])).Exit);
        var page = SendOk($$$"""{"cmd":"events.read","args":{"run":"{{{run}}}","after":"{{{after}}}","kinds":["status"]}}""");
        var expected = AgTerm.Core.Protocol.ControlJson.Deserialize<AgTerm.Core.Protocol.ControlEvent>(
            page["events"]!["items"]!.AsArray()[^1]!.ToJsonString())!;
        using var process = Process.Start(StartInfo(["events", "--json", "--run", run, "--after", after.ToString(System.Globalization.CultureInfo.InvariantCulture), "--kind", "status"]))!;
        try
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(AgTerm.Core.Protocol.ControlJson.Serialize(expected), line);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static string Executable => Environment.GetEnvironmentVariable("AGTERMCTL_PUBLISHED_EXE")
        ?? throw new InvalidOperationException("AGTERMCTL_PUBLISHED_EXE must name the published agtermctl.exe.");

    private ProcessStartInfo StartInfo(string[] arguments)
    {
        Assert.True(File.Exists(Executable), $"Published CLI not found: {Executable}");
        var info = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["AGTERM_CONTROL_SOCKET"] = Host.PipeName;
        return info;
    }

    private async Task<(int Exit, string Out, string Error)> RunPublished(string[] arguments, string stdin = "")
    {
        using var process = Process.Start(StartInfo(arguments))!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}

public sealed class PublishedCliFactAttribute : FactAttribute
{
    public PublishedCliFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AGTERMCTL_PUBLISHED_EXE")))
            Skip = "Set AGTERMCTL_PUBLISHED_EXE to test a native published executable.";
    }
}
