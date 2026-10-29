using System.Runtime.InteropServices;
using AgTerm.Core.Model;

namespace AgTerm.App;

public enum NotificationSuppressionState
{
    Failed = 0, NotPresent = 1, Busy = 2, FullScreen = 3, Presentation = 4,
    AcceptsNotifications = 5, QuietTime = 6, App = 7, Disabled = 8, Restricted = 9,
}

public interface INotificationSuppression
{
    NotificationSuppressionState Query();
}

public sealed class ShellNotificationSuppression : INotificationSuppression
{
    public NotificationSuppressionState Query() =>
        SHQueryUserNotificationState(out var state) == 0 ? state : NotificationSuppressionState.Failed;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out NotificationSuppressionState state);
}

public enum NotificationSoundKind { Status, Notify, Preview }

public sealed class WindowsNotificationSound
{
    private static readonly Lock QueueGate = new();
    private static Task tail = Task.CompletedTask;
    private readonly SoundCatalog catalog;
    private readonly INotificationSuppression suppression;
    private readonly Func<CatalogSound, bool> play;
    private readonly Action<string> log;

    public WindowsNotificationSound(SoundCatalog catalog, INotificationSuppression? suppression = null,
        Func<CatalogSound, bool>? play = null, Action<string>? log = null)
    {
        this.catalog = catalog;
        this.suppression = suppression ?? new ShellNotificationSuppression();
        this.play = play ?? Play;
        this.log = log ?? (message => System.Diagnostics.Trace.TraceWarning(message));
    }

    public Task<bool> TryPlayAsync(string? name, NotificationSoundKind kind = NotificationSoundKind.Status)
    {
        if (string.IsNullOrEmpty(name)) return Task.FromResult(true);
        lock (QueueGate)
        {
            var next = tail.ContinueWith(_ => PlaySelected(name, kind), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            tail = next;
            return next;
        }
    }

    private bool PlaySelected(string name, NotificationSoundKind kind)
    {
        if (kind == NotificationSoundKind.Notify)
        {
            var state = suppression.Query();
            if (state == NotificationSuppressionState.Failed)
                log("Could not query Windows notification suppression; notify sound was suppressed.");
            if (state is not (NotificationSuppressionState.AcceptsNotifications or NotificationSuppressionState.App))
                return true;
        }
        var sound = catalog.Resolve(name);
        if (sound is null)
        {
            log($"Sound '{name}' is unavailable; " + (kind == NotificationSoundKind.Notify
                ? "notify will use the Windows default sound." : "no sound was played."));
            if (kind != NotificationSoundKind.Notify) return false;
            sound = new("default", null);
        }
        if (play(sound)) return true;
        log($"Windows could not play sound '{name}'.");
        if (kind == NotificationSoundKind.Notify && !sound.IsSystemDefault)
        {
            log("notify will use the Windows default sound.");
            if (play(new("default", null))) return true;
            log("Windows could not play the default notification sound.");
        }
        return false;
    }

    private static bool Play(CatalogSound sound) =>
        PlaySoundW(sound.FilePath ?? "SystemDefault", IntPtr.Zero,
            0x0002u | (sound.IsSystemDefault ? 0x00010000u : 0x00020000u));

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(string sound, IntPtr module, uint flags);
}