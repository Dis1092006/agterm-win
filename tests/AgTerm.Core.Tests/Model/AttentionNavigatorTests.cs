using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>Cross-window attention stepping (upstream #824 WindowLibraryAttentionTests).</summary>
public sealed class AttentionNavigatorTests
{
    private static readonly Guid W1 = Guid.NewGuid();
    private static readonly Guid W2 = Guid.NewGuid();
    private static readonly Guid W3 = Guid.NewGuid();

    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();
    private static readonly Guid E = Guid.NewGuid();

    private static AttentionWindow Window(Guid id, bool eligible, params (Guid Id, bool Stops)[] sessions) =>
        new(id, eligible, sessions.Select(s => new AttentionCandidate(s.Id, s.Stops)).ToList());

    [Fact]
    public void Steps_forward_into_the_next_window_in_library_order()
    {
        var windows = new[] { Window(W1, true, (A, false), (B, false)), Window(W2, true, (C, true)) };
        Assert.Equal((W2, C), AttentionNavigator.Step(windows, W1, A, forward: true));
    }

    [Fact]
    public void Steps_backward_into_the_previous_window()
    {
        var windows = new[] { Window(W1, true, (A, true)), Window(W2, true, (B, false), (C, false)) };
        Assert.Equal((W1, A), AttentionNavigator.Step(windows, W2, B, forward: false));
    }

    [Fact]
    public void Wraps_around_the_library_in_both_directions()
    {
        var windows = new[] { Window(W1, true, (A, true), (B, false)), Window(W2, true, (C, false), (D, true)) };
        Assert.Equal((W1, A), AttentionNavigator.Step(windows, W2, D, forward: true));
        Assert.Equal((W2, D), AttentionNavigator.Step(windows, W1, A, forward: false));
    }

    [Fact]
    public void Excludes_the_current_session_and_returns_null_when_it_is_the_only_stop()
    {
        var windows = new[] { Window(W1, true, (A, true), (B, false)) };
        Assert.Null(AttentionNavigator.Step(windows, W1, A, forward: true));
        Assert.Null(AttentionNavigator.Step(windows, W1, A, forward: false));
    }

    [Fact]
    public void Returns_null_when_nothing_stops()
    {
        var windows = new[] { Window(W1, true, (A, false)), Window(W2, true, (B, false)) };
        Assert.Null(AttentionNavigator.Step(windows, W1, A, forward: true));
    }

    [Fact]
    public void Skips_an_ineligible_window_and_keeps_scanning()
    {
        var windows = new[]
        {
            Window(W1, true, (A, false)),
            Window(W2, false, (B, true)),
            Window(W3, true, (C, true)),
        };
        Assert.Equal((W3, C), AttentionNavigator.Step(windows, W1, A, forward: true));
    }

    [Fact]
    public void The_source_window_is_included_even_when_it_is_ineligible()
    {
        var windows = new[] { Window(W1, false, (A, false), (B, true)), Window(W2, true, (C, false)) };
        Assert.Equal((W1, B), AttentionNavigator.Step(windows, W1, A, forward: true));
    }

    [Fact]
    public void Without_a_selection_enters_at_the_first_candidate_forward_and_the_last_backward()
    {
        var windows = new[] { Window(W1, true, (A, true), (B, false)), Window(W2, true, (C, false), (D, true)) };
        Assert.Equal((W1, A), AttentionNavigator.Step(windows, W1, null, forward: true));
        Assert.Equal((W2, D), AttentionNavigator.Step(windows, W1, null, forward: false));
    }

    [Fact]
    public void A_selection_missing_from_the_source_window_enters_like_no_selection()
    {
        var windows = new[] { Window(W1, true, (A, true)), Window(W2, true, (B, true)) };
        Assert.Equal((W1, A), AttentionNavigator.Step(windows, W1, E, forward: true));
    }

    [Fact]
    public void Same_window_target_is_reported_with_the_source_window_id()
    {
        var windows = new[] { Window(W1, true, (A, false), (B, true)), Window(W2, true, (C, true)) };
        Assert.Equal((W1, B), AttentionNavigator.Step(windows, W1, A, forward: true));
    }

    [Fact]
    public void An_unknown_source_window_still_scans_the_eligible_windows()
    {
        var windows = new[] { Window(W1, true, (A, true)) };
        Assert.Equal((W1, A), AttentionNavigator.Step(windows, W2, null, forward: true));
    }
}
