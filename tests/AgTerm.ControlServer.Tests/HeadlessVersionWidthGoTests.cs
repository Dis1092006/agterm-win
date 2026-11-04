using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>version, sidebar.width, workspace.go and window.go over the pipe against the headless
/// host (upstream 5d22904a, 7977f202, 3624dd05, 99c8c50d).</summary>
public sealed class HeadlessVersionWidthGoTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessVersionWidthGoTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), "agterm-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDir);
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
        });
        _host.Start();
        _client = new ControlPipeClient(_host.PipeName);
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ControlResponse Send(Command cmd, ControlArgs? args = null, string? target = null) =>
        _client.Send(new ControlRequest { Cmd = cmd, Target = target, Args = args });

    private Guid NewWorkspace(string name)
    {
        var created = Send(Command.WorkspaceNew, new ControlArgs { Name = name });
        Assert.True(created.Ok, created.Error);
        return Guid.Parse(created.Result!.Id!);
    }

    [Fact]
    public void VersionReportsTheRunningBuildAndNoInstalledIdentity()
    {
        var response = Send(Command.Version);

        Assert.True(response.Ok, response.Error);
        Assert.False(string.IsNullOrWhiteSpace(response.Result!.App!.Version));
        Assert.Null(response.Result.Installed);
    }

    [Fact]
    public void SidebarWidthStoresAndEchoesTheWidth()
    {
        var response = Send(Command.SidebarWidth, new ControlArgs { SidebarWidth = 300 });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(300, response.Result!.SidebarWidth);
        Assert.Equal(300, _host.Store.SidebarWidth);
    }

    [Theory]
    [InlineData(10, 160)]
    [InlineData(9000, 560)]
    public void SidebarWidthEchoesTheClampedWidth(double asked, double stored)
    {
        var response = Send(Command.SidebarWidth, new ControlArgs { SidebarWidth = asked });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(stored, response.Result!.SidebarWidth);
        Assert.Equal(stored, _host.Store.SidebarWidth);
    }

    [Fact]
    public void SidebarWidthWithoutAWidthIsRefused()
    {
        var response = Send(Command.SidebarWidth);

        Assert.False(response.Ok);
        Assert.Equal("sidebar.width requires a width in points", response.Error);
    }

    [Fact]
    public void SidebarWidthAnUnknownWindowIsRefused()
    {
        var response = Send(Command.SidebarWidth, new ControlArgs { SidebarWidth = 300, Window = "nope" });

        Assert.False(response.Ok);
        Assert.Equal("no such window: nope", response.Error);
    }

    [Fact]
    public void WorkspaceGoStepsForwardAndWrapsAroundAndBack()
    {
        var first = _host.Store.CurrentWorkspaceId!.Value;
        var second = NewWorkspace("second");
        var third = NewWorkspace("third");
        Assert.True(Send(Command.WorkspaceSelect, target: first.ToString()).Ok);

        Assert.Equal(second.ToString("D").ToUpperInvariant(),
            Send(Command.WorkspaceGo, new ControlArgs { To = "next" }).Result!.Id);
        Assert.Equal(third.ToString("D").ToUpperInvariant(),
            Send(Command.WorkspaceGo, new ControlArgs { To = "next" }).Result!.Id);
        Assert.Equal(first.ToString("D").ToUpperInvariant(),
            Send(Command.WorkspaceGo, new ControlArgs { To = "next" }).Result!.Id);
        Assert.Equal(third.ToString("D").ToUpperInvariant(),
            Send(Command.WorkspaceGo, new ControlArgs { To = "prev" }).Result!.Id);
        Assert.Equal(third, _host.Store.CurrentWorkspaceId);
    }

    [Fact]
    public void WorkspaceGoWithOneWorkspaceIsRefused()
    {
        var response = Send(Command.WorkspaceGo, new ControlArgs { To = "next" });

        Assert.False(response.Ok);
        Assert.Equal("no other workspace to navigate to", response.Error);
    }

    [Fact]
    public void WorkspaceGoWithoutADirectionIsRefused()
    {
        var response = Send(Command.WorkspaceGo);

        Assert.False(response.Ok);
        Assert.Equal("workspace.go requires --to next|prev", response.Error);
    }

    [Fact]
    public void WindowGoHasNoOtherOpenWindowToReach()
    {
        var response = Send(Command.WindowGo, new ControlArgs { To = "next" });

        Assert.False(response.Ok);
        Assert.Equal("no other open window to navigate to", response.Error);
    }

    [Fact]
    public void WindowGoWithoutADirectionIsRefused()
    {
        var response = Send(Command.WindowGo);

        Assert.False(response.Ok);
        Assert.Equal("window.go requires --to next|prev", response.Error);
    }
}
