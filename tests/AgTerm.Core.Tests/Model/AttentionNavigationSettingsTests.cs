using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

/// <summary>The attentionIncludesActive and attentionNavigationAcrossWindows settings (upstream #807, #824).</summary>
public sealed class AttentionNavigationSettingsTests
{
    private static (AppStore Store, List<Guid> Ids) MakeTree()
    {
        var store = AppStoreFixtures.MakeStore();
        var work = store.AddWorkspace("work");
        var personal = store.AddWorkspace("personal");
        var ids = new List<Guid>
        {
            store.AddSession(work.Id, "/a")!.Id,
            store.AddSession(work.Id, "/b")!.Id,
            store.AddSession(personal.Id, "/c")!.Id,
            store.AddSession(personal.Id, "/d")!.Id,
        };
        return (store, ids);
    }

    private static void Mark(AppStore store, Guid id, AgentStatus status) =>
        store.SessionWithId(id)!.AgentIndicator = new AgentIndicator(status);

    [Fact]
    public void Both_settings_are_off_when_absent()
    {
        var settings = new AppSettings();
        Assert.False(settings.EffectiveAttentionIncludesActive);
        Assert.False(settings.EffectiveAttentionNavigationAcrossWindows);
    }

    [Fact]
    public void Both_settings_round_trip_under_the_upstream_names()
    {
        var json = ControlJson.Serialize(new AppSettings
        {
            AttentionIncludesActive = true,
            AttentionNavigationAcrossWindows = true,
        });
        Assert.Contains("\"attentionIncludesActive\":true", json);
        Assert.Contains("\"attentionNavigationAcrossWindows\":true", json);
        Assert.DoesNotContain("Effective", json);
        var decoded = ControlJson.Deserialize<AppSettings>(json)!;
        Assert.True(decoded.EffectiveAttentionIncludesActive);
        Assert.True(decoded.EffectiveAttentionNavigationAcrossWindows);
    }

    [Fact]
    public void Both_settings_take_part_in_equality()
    {
        Assert.NotEqual(new AppSettings(), new AppSettings { AttentionIncludesActive = true });
        Assert.NotEqual(new AppSettings(), new AppSettings { AttentionNavigationAcrossWindows = true });
        Assert.Equal(new AppSettings { AttentionIncludesActive = true },
            new AppSettings { AttentionIncludesActive = true });
    }

    [Fact]
    public void Next_attention_skips_an_active_session_by_default()
    {
        var (store, ids) = MakeTree();
        Mark(store, ids[1], AgentStatus.Active);
        Mark(store, ids[2], AgentStatus.Blocked);
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[2], store.SelectedSessionId);
    }

    [Fact]
    public void Next_attention_stops_on_an_active_session_when_the_setting_is_on()
    {
        var (store, ids) = MakeTree();
        store.AttentionIncludesActive = true;
        Mark(store, ids[1], AgentStatus.Active);
        Mark(store, ids[2], AgentStatus.Blocked);
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[1], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[2], store.SelectedSessionId);
    }

    [Fact]
    public void Previous_attention_stops_on_an_active_session_when_the_setting_is_on()
    {
        var (store, ids) = MakeTree();
        store.AttentionIncludesActive = true;
        Mark(store, ids[1], AgentStatus.Active);
        Mark(store, ids[2], AgentStatus.Completed);
        store.SelectSession(ids[3]);
        store.NavigateSession(SessionNavigation.PreviousAttention);
        Assert.Equal(ids[2], store.SelectedSessionId);
        store.NavigateSession(SessionNavigation.PreviousAttention);
        Assert.Equal(ids[1], store.SelectedSessionId);
    }

    [Fact]
    public void An_idle_session_is_still_skipped_when_the_setting_is_on()
    {
        var (store, ids) = MakeTree();
        store.AttentionIncludesActive = true;
        Mark(store, ids[3], AgentStatus.Blocked);
        store.SelectSession(ids[0]);
        store.NavigateSession(SessionNavigation.NextAttention);
        Assert.Equal(ids[3], store.SelectedSessionId);
    }

    [Fact]
    public void The_setting_leaves_the_attention_list_and_needs_attention_alone()
    {
        var (store, ids) = MakeTree();
        store.AttentionIncludesActive = true;
        Mark(store, ids[1], AgentStatus.Active);
        Assert.False(AgentStatus.Active.NeedsAttention());
        Assert.Empty(store.AttentionSessions.Where(s => s.AgentIndicator.Status == AgentStatus.Active)
            .Where(s => s.AgentIndicator.Status.NeedsAttention()));
    }

    [Theory]
    [InlineData(AgentStatus.Idle, false, false)]
    [InlineData(AgentStatus.Active, false, false)]
    [InlineData(AgentStatus.Blocked, false, true)]
    [InlineData(AgentStatus.Completed, false, true)]
    [InlineData(AgentStatus.Idle, true, false)]
    [InlineData(AgentStatus.Active, true, true)]
    [InlineData(AgentStatus.Blocked, true, true)]
    [InlineData(AgentStatus.Completed, true, true)]
    public void StopsAttentionNavigation_follows_the_setting(AgentStatus status, bool includesActive, bool expected)
    {
        var (store, ids) = MakeTree();
        store.AttentionIncludesActive = includesActive;
        Mark(store, ids[0], status);
        Assert.Equal(expected, store.StopsAttentionNavigation(store.SessionWithId(ids[0])!));
    }

    [Fact]
    public void The_tree_reports_both_settings_after_autoFollowMs()
    {
        var (store, _) = MakeTree();
        var tree = store.ControlTree();
        Assert.False(tree.AttentionIncludesActive);
        Assert.False(tree.AttentionNavigationAcrossWindows);

        store.AttentionIncludesActive = true;
        store.AttentionNavigationAcrossWindows = true;
        tree = store.ControlTree();
        Assert.True(tree.AttentionIncludesActive);
        Assert.True(tree.AttentionNavigationAcrossWindows);

        var json = ControlJson.Serialize(tree);
        var autoFollow = json.IndexOf("\"idleMs\"", StringComparison.Ordinal);
        var includes = json.IndexOf("\"attentionIncludesActive\":true", StringComparison.Ordinal);
        var across = json.IndexOf("\"attentionNavigationAcrossWindows\":true", StringComparison.Ordinal);
        var sidebar = json.IndexOf("\"sidebarVisible\"", StringComparison.Ordinal);
        Assert.True(autoFollow < includes && includes < across && across < sidebar, json);
    }

    [Fact]
    public void The_settings_editor_lists_both_toggles_in_agent_status()
    {
        foreach (var key in new[] { "attentionIncludesActive", "attentionNavigationAcrossWindows" })
        {
            var field = SettingsEditor.Fields.Single(f => f.Key == key);
            Assert.Equal("Agent Status", field.Tab);
            Assert.Equal(SettingControl.Toggle, field.Control);
        }
    }
}
