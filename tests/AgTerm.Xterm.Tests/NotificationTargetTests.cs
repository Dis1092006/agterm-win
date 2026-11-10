using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class NotificationTargetTests
{
    [Theory]
    [InlineData(NotificationPane.Main, "main")]
    [InlineData(NotificationPane.Split, "split")]
    public void Identity_roundtrips_and_toast_tags_group_each_sessions_panes(NotificationPane pane, string role)
    {
        var target = new LiveNotificationTarget(Guid.NewGuid(), Guid.NewGuid(), pane);
        Assert.Equal($"{target.WindowId:D}:{target.SessionId:D}:".ToUpperInvariant() + role,
            target.Identity);
        Assert.Equal(target, LiveNotificationTarget.Parse(target.Identity));
        Assert.Equal(target.SessionId.ToString("D").ToUpperInvariant(), target.ToastGroup);
        Assert.Equal(target.WindowId.ToString("D").ToUpperInvariant() + ":" + role, target.ToastTag);
        Assert.True(target.ToastTag.Length <= 64 && target.ToastGroup.Length <= 64);
        Assert.NotEqual(target.ToastTag, (target with { Pane = pane == NotificationPane.Main
            ? NotificationPane.Split : NotificationPane.Main }).ToastTag);
    }

    [Fact]
    public void Control_notify_has_its_own_slot_in_the_session_group_and_clicks_to_the_main_pane()
    {
        var target = new LiveNotificationTarget(Guid.NewGuid(), Guid.NewGuid(), NotificationPane.Main, Control: true);
        Assert.Equal($"{target.WindowId:D}:{target.SessionId:D}:".ToUpperInvariant() + "notify", target.Identity);
        Assert.Equal(target.WindowId.ToString("D").ToUpperInvariant() + ":notify", target.ToastTag);
        Assert.Equal(target.SessionId.ToString("D").ToUpperInvariant(), target.ToastGroup);
        Assert.True(target.ToastTag.Length <= 64);

        var parsed = LiveNotificationTarget.Parse(target.Identity);
        Assert.Equal(target, parsed);
        Assert.Equal(NotificationPane.Main, parsed!.Pane);

        foreach (var pane in new[] { NotificationPane.Main, NotificationPane.Split })
            Assert.NotEqual(target.ToastTag, new LiveNotificationTarget(target.WindowId, target.SessionId, pane).ToastTag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000001:00000000-0000-0000-0000-000000000002:NOTIFY")]
    [InlineData("bad:target:main")]
    [InlineData("00000000-0000-0000-0000-000000000001:00000000-0000-0000-0000-000000000002:MAIN")]
    [InlineData("00000000-0000-0000-0000-000000000001:00000000-0000-0000-0000-000000000002:scratch")]
    [InlineData("00000000-0000-0000-0000-000000000001:00000000-0000-0000-0000-000000000002:main:extra")]
    public void Invalid_targets_cannot_route_to_other_commands_or_panes(string identity)
    {
        Assert.Null(LiveNotificationTarget.Parse(identity));
    }
}
