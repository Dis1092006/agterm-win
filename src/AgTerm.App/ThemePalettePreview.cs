using System.Runtime.ExceptionServices;

namespace AgTerm.App;

/// <summary>Runs the debounced preview work. The window's scheduler debounces on a timer and runs
/// the work on its UI thread; tests run it by hand.</summary>
public interface IThemePreviewScheduler
{
    void Schedule(Func<Task> work);

    void Cancel();
}

/// <summary>The live preview of the theme palette for one window. A preview only changes what the
/// window shows; the window's saved settings are never written, so a cancel shows them again. Every
/// Begin, Select, Commit and Cancel starts a new generation, and a show checks that its generation
/// is still current after it waits for the window's theme gate, so a stale show never lands on top
/// of a newer one or of a cancel.</summary>
public sealed class ThemePalettePreview
{
    private readonly IThemePreviewScheduler _scheduler;
    private readonly Func<string?, Func<bool>, Task> _showThemeAsync;
    private readonly Func<Task> _showSavedAsync;
    private int _generation;

    public ThemePalettePreview(
        IThemePreviewScheduler scheduler,
        Func<string?, Func<bool>, Task> showThemeAsync,
        Func<Task> showSavedAsync)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(showThemeAsync);
        ArgumentNullException.ThrowIfNull(showSavedAsync);
        _scheduler = scheduler;
        _showThemeAsync = showThemeAsync;
        _showSavedAsync = showSavedAsync;
    }

    public bool IsActive { get; private set; }

    public void Begin()
    {
        _scheduler.Cancel();
        _generation++;
        IsActive = true;
    }

    public void Select(string? themeName)
    {
        if (!IsActive)
            return;

        var generation = ++_generation;
        _scheduler.Schedule(() => _showThemeAsync(themeName, () => IsActive && _generation == generation));
    }

    /// <summary>Enter or click. The accepted row is shown at once because a debounced move may still
    /// be pending, and theme.set raises no theme event when the saved theme does not change. It saves
    /// even without an active preview: an accept must never be dropped. The preview ends before the
    /// first await, so a palette close that runs right after finds nothing to cancel. A show that
    /// throws still lets the save run, and a save that fails or throws shows the saved look again,
    /// because the preview has ended and no cancel would restore it later; the exception is rethrown
    /// after that for the window's fault observer.</summary>
    public async Task CommitAsync(string? themeName, Func<Task<bool>> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);

        var generation = End();
        var showFailure = await TryShowAsync(themeName, () => _generation == generation);
        await SaveOrShowSavedAsync(saveAsync);
        showFailure?.Throw();
    }

    public async Task CancelAsync()
    {
        if (!IsActive)
            return;

        End();
        await _showSavedAsync();
    }

    private async Task<ExceptionDispatchInfo?> TryShowAsync(string? themeName, Func<bool> isCurrent)
    {
        try
        {
            await _showThemeAsync(themeName, isCurrent);
            return null;
        }
        catch (Exception exception)
        {
            return ExceptionDispatchInfo.Capture(exception);
        }
    }

    private async Task SaveOrShowSavedAsync(Func<Task<bool>> saveAsync)
    {
        bool saved;
        try
        {
            saved = await saveAsync();
        }
        catch
        {
            await _showSavedAsync();
            throw;
        }

        if (!saved)
            await _showSavedAsync();
    }

    private int End()
    {
        _scheduler.Cancel();
        IsActive = false;
        return ++_generation;
    }
}
