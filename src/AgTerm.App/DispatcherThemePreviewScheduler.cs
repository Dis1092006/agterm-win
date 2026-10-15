using AgTerm.Core.Model;
using Microsoft.UI.Dispatching;

namespace AgTerm.App;

/// <summary>Debounces palette preview applies the way upstream's previewTheme does (about 0.07 s),
/// so a held arrow key applies only the row it stops on. Debouncer fires on the thread pool, and
/// the page and the chrome belong to the UI thread.</summary>
internal sealed class DispatcherThemePreviewScheduler : IThemePreviewScheduler
{
    private const double DebounceSeconds = 0.07;

    private readonly Debouncer _debouncer = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Action<Task> _observe;

    public DispatcherThemePreviewScheduler(DispatcherQueue dispatcher, Action<Task> observe)
    {
        _dispatcher = dispatcher;
        _observe = observe;
    }

    public void Schedule(Func<Task> work) =>
        _debouncer.Schedule(DebounceSeconds, () => _dispatcher.TryEnqueue(() => _observe(work())));

    public void Cancel() => _debouncer.Cancel();
}
