using AgTerm.ControlServer;
using AgTerm.Core.Icons;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public sealed class HeadlessWorkspaceIconTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessWorkspaceIconTests()
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

    private ControlResponse Send(ControlRequest request) => _client.Send(request);

    private ControlResponse SetIcon(string? name, string? target = null) =>
        Send(new ControlRequest
        {
            Cmd = Command.WorkspaceIcon,
            Target = target,
            Args = name is null ? null : new ControlArgs { Name = name },
        });

    private ControlWorkspaceNode Workspace()
    {
        var tree = Send(new ControlRequest { Cmd = Command.Tree });
        Assert.True(tree.Ok, tree.Error);
        return Assert.Single(tree.Result!.Tree!.Workspaces);
    }

    [Fact]
    public void A_known_name_is_set_and_tree_shows_it()
    {
        var id = Workspace().Id;

        var response = SetIcon("MapPin");

        Assert.Equal($$$"""{"ok":true,"result":{"id":"{{{id}}}"}}""", ControlJson.Serialize(response));
        Assert.Contains("\"icon\":\"MapPin\"", ControlJson.Serialize(Workspace()));
    }

    [Fact]
    public void A_name_in_the_wrong_case_is_refused_and_changes_nothing()
    {
        var response = SetIcon("mappin");

        Assert.Equal("""{"ok":false,"error":"unknown icon: mappin"}""", ControlJson.Serialize(response));
        Assert.Null(Workspace().Icon);
    }

    [Fact]
    public void The_name_is_trimmed_before_it_is_looked_up()
    {
        Assert.True(SetIcon("  MapPin  ").Ok);

        Assert.Equal("MapPin", Workspace().Icon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_name_clears_the_icon(string? name)
    {
        Assert.True(SetIcon("MapPin").Ok);

        var response = SetIcon(name);

        Assert.True(response.Ok, response.Error);
        Assert.DoesNotContain("\"icon\"", ControlJson.Serialize(Workspace()));
    }

    [Fact]
    public void An_unknown_target_answers_the_error_workspace_theme_gives()
    {
        var theme = Send(new ControlRequest
        {
            Cmd = Command.WorkspaceTheme, Target = "nope", Args = new ControlArgs { Name = "Nord" },
        });

        var icon = SetIcon("MapPin", target: "nope");

        Assert.False(icon.Ok);
        Assert.Equal(NoSuchWorkspace, theme.Error);
        Assert.Equal(NoSuchWorkspace, icon.Error);
    }

    [Fact]
    public void An_unknown_target_is_reported_before_an_unknown_name()
    {
        var response = SetIcon("mappin", target: "nope");

        Assert.Equal(NoSuchWorkspace, response.Error);
    }

    [Fact]
    public void Icon_list_answers_every_catalog_name_and_no_other_field()
    {
        var response = Send(new ControlRequest { Cmd = Command.IconList });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(1533, response.Result!.Icons!.Length);
        Assert.Equal("GlobalNavButton", response.Result.Icons[0]);
        Assert.Equal(SegoeIconCatalog.All.Select(icon => icon.Name), response.Result.Icons);
        Assert.StartsWith("""{"ok":true,"result":{"icons":["GlobalNavButton",""", ControlJson.Serialize(response));
        Assert.Equal(
            ControlJson.Serialize(new ControlResult { Icons = response.Result.Icons }),
            ControlJson.Serialize(response.Result));
    }

    private const string NoSuchWorkspace = "no such workspace: nope";
}
