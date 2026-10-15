using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class ThemePalettePreviewTests
{
    private sealed class ManualScheduler : IThemePreviewScheduler
    {
        private Func<Task>? _pending;

        public void Schedule(Func<Task> work) => _pending = work;

        public void Cancel() => _pending = null;

        public async Task RunPendingAsync()
        {
            var work = _pending;
            _pending = null;
            if (work is not null)
                await work();
        }
    }

    private sealed class Window
    {
        public List<string> Shown { get; } = [];

        public List<Func<bool>> CurrentChecks { get; } = [];

        public Task ShowThemeAsync(string? name, Func<bool> isCurrent)
        {
            CurrentChecks.Add(isCurrent);
            if (isCurrent())
                Shown.Add(name ?? "<default>");
            return Task.CompletedTask;
        }

        public Task ShowSavedAsync()
        {
            Shown.Add("<saved>");
            return Task.CompletedTask;
        }
    }

    private static (ThemePalettePreview Preview, ManualScheduler Scheduler, Window Window) Create()
    {
        var scheduler = new ManualScheduler();
        var window = new Window();
        return (new ThemePalettePreview(scheduler, window.ShowThemeAsync, window.ShowSavedAsync), scheduler, window);
    }

    [Fact]
    public async Task Select_before_begin_shows_nothing()
    {
        var (preview, scheduler, window) = Create();

        preview.Select("Nord");
        await scheduler.RunPendingAsync();

        Assert.False(preview.IsActive);
        Assert.Empty(window.Shown);
    }

    [Fact]
    public async Task Quick_moves_show_only_the_last_row_after_the_debounce()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();

        preview.Select("A");
        preview.Select("B");
        preview.Select(null);
        Assert.Empty(window.Shown);
        await scheduler.RunPendingAsync();

        Assert.Equal(["<default>"], window.Shown);
    }

    [Fact]
    public async Task Cancel_drops_the_pending_move_and_shows_the_saved_look()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("A");

        await preview.CancelAsync();
        await scheduler.RunPendingAsync();

        Assert.False(preview.IsActive);
        Assert.Equal(["<saved>"], window.Shown);
    }

    [Fact]
    public async Task Cancel_when_not_active_does_nothing()
    {
        var (preview, _, window) = Create();

        await preview.CancelAsync();

        Assert.Empty(window.Shown);
    }

    [Fact]
    public async Task Commit_shows_the_accepted_row_at_once_even_with_a_move_pending()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("Other");

        await preview.CommitAsync("Saved", () => Task.FromResult(true));
        await scheduler.RunPendingAsync();

        Assert.Equal(["Saved"], window.Shown);
        Assert.False(preview.IsActive);
    }

    [Fact]
    public async Task Commit_without_a_preview_still_shows_and_saves()
    {
        var (preview, _, window) = Create();
        var saved = false;

        await preview.CommitAsync("Nord", () => { saved = true; return Task.FromResult(true); });

        Assert.True(saved);
        Assert.Equal(["Nord"], window.Shown);
    }

    [Fact]
    public async Task Commit_ends_the_preview_before_it_awaits()
    {
        var show = new TaskCompletionSource();
        var preview = new ThemePalettePreview(new ManualScheduler(), (_, _) => show.Task, () => Task.CompletedTask);
        preview.Begin();

        var commit = preview.CommitAsync("Nord", () => Task.FromResult(true));

        Assert.False(preview.IsActive);
        show.SetResult();
        await commit;
    }

    [Fact]
    public async Task A_show_that_throws_still_saves_and_then_throws()
    {
        var failure = new InvalidOperationException("show failed");
        var preview = new ThemePalettePreview(new ManualScheduler(), (_, _) => throw failure, () => Task.CompletedTask);
        preview.Begin();
        var saved = false;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preview.CommitAsync("Nord", () => { saved = true; return Task.FromResult(true); }));

        Assert.Same(failure, thrown);
        Assert.True(saved);
    }

    [Fact]
    public async Task A_save_that_throws_shows_the_saved_look_and_then_throws()
    {
        var (preview, _, window) = Create();
        preview.Begin();
        var failure = new InvalidOperationException("save failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preview.CommitAsync("Nord", () => throw failure));

        Assert.Same(failure, thrown);
        Assert.Equal(["Nord", "<saved>"], window.Shown);
    }

    [Fact]
    public async Task A_cancel_after_a_commit_does_not_restore()
    {
        var (preview, _, window) = Create();
        preview.Begin();

        await preview.CommitAsync("Nord", () => Task.FromResult(true));
        await preview.CancelAsync();

        Assert.Equal(["Nord"], window.Shown);
    }

    [Fact]
    public async Task A_failed_save_shows_the_saved_look_again()
    {
        var (preview, _, window) = Create();
        preview.Begin();

        await preview.CommitAsync("Gone", () => Task.FromResult(false));

        Assert.Equal(["Gone", "<saved>"], window.Shown);
    }

    [Fact]
    public async Task A_show_that_waits_past_a_cancel_is_no_longer_current()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("A");
        await scheduler.RunPendingAsync();
        var isCurrent = window.CurrentChecks.Single();

        await preview.CancelAsync();

        Assert.False(isCurrent());
    }

    [Fact]
    public async Task A_newer_move_makes_an_older_show_stale()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("A");
        await scheduler.RunPendingAsync();
        var older = window.CurrentChecks.Single();

        preview.Select("B");

        Assert.False(older());
    }

    [Fact]
    public async Task A_new_begin_drops_the_older_pending_move()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("A");

        preview.Begin();
        await scheduler.RunPendingAsync();

        Assert.Empty(window.Shown);
        Assert.True(preview.IsActive);
    }

    [Fact]
    public async Task Abandon_drops_the_pending_move_and_shows_nothing()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("A");

        preview.Abandon();
        await scheduler.RunPendingAsync();

        Assert.False(preview.IsActive);
        Assert.Empty(window.Shown);
    }

    [Fact]
    public async Task A_show_that_left_the_debounce_before_an_abandon_is_no_longer_current()
    {
        var (preview, scheduler, window) = Create();
        preview.Begin();
        preview.Select("A");
        await scheduler.RunPendingAsync();
        var isCurrent = window.CurrentChecks.Single();

        preview.Abandon();

        Assert.False(isCurrent());
        Assert.Equal(["A"], window.Shown);
    }

    [Fact]
    public void Abandon_when_not_active_does_nothing()
    {
        var (preview, _, window) = Create();

        preview.Abandon();

        Assert.False(preview.IsActive);
        Assert.Empty(window.Shown);
    }
}
