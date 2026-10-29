using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer.Tests;

public sealed class StatusSoundTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-status-sound-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Unknown_sound_is_rejected_before_target_resolution_without_mutation_or_events()
    {
        using var host = new HeadlessHost(new() { StateDir = directory });
        var id = host.Store.SelectedSessionId!.Value;
        var before = host.Store.SessionWithId(id)!.AgentIndicator;
        var anchor = host.Ring.Read(null).Batch;
        var response = host.Actions.SetSessionStatus(id.ToString(), null, Update("missing-agterm-test-sound"));
        Assert.False(response.Ok);
        Assert.StartsWith("unknown sound: missing-agterm-test-sound (use 'default', 'beep', or one of: ", response.Error);
        Assert.Equal(before, host.Store.SessionWithId(id)!.AgentIndicator);
        Assert.Equal(response.Error,
            host.Actions.SetSessionStatus("not-a-session", null, Update("missing-agterm-test-sound")).Error);
        Assert.Empty(host.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch.Items);
    }

    [Fact]
    public void Blocked_default_is_requested_only_on_a_transition_but_explicit_sound_always_is()
    {
        var requests = new List<LiveStatusNotificationRequest>();
        using var host = new HeadlessHost(new() { StateDir = directory, StatusNotificationRequested = requests.Add });
        Assert.True(host.Actions.SetSessionStatus(null, null, Update(null)).Ok);
        Assert.Single(requests);
        Assert.True(host.Actions.SetSessionStatus(null, null, Update("")).Ok);
        Assert.Single(requests);
        Assert.True(host.Actions.SetSessionStatus(null, null, Update("default")).Ok);
        Assert.Equal(2, requests.Count);
    }

    private static ControlSessionStatusUpdate Update(string? sound) =>
        new(AgentStatus.Blocked, null, null, sound);
}
