using System.Runtime.InteropServices;

namespace AgTerm.App;

public interface INotificationSuppressionSources
{
    bool NotificationsEnabled();
    bool HasNotificationMode();
    int NotificationMode();
    uint? QuietHoursProfile();
    NotificationSuppressionState ShellState();
}

public sealed class NotificationSuppression(INotificationSuppressionSources sources, Action<string>? log = null)
    : INotificationSuppression
{
    private readonly Action<string> log = log ?? (message => System.Diagnostics.Trace.TraceWarning(message));

    public NotificationSuppressionState Query()
    {
        try
        {
            if (!sources.NotificationsEnabled()) return NotificationSuppressionState.Disabled;
            if (sources.HasNotificationMode())
            {
                if (sources.NotificationMode() != 0) return NotificationSuppressionState.Restricted;
            }
            else
            {
                uint? profile;
                try { profile = sources.QuietHoursProfile(); }
                catch (Exception ex) when (IsQueryFailure(ex))
                {
                    log($"Could not read Windows quiet-hours WNF state; continuing with the shell check: {ex.Message}");
                    return sources.ShellState();
                }
                if (profile is null)
                    log("Could not read Windows quiet-hours WNF state; continuing with the shell check.");
                else if (profile != 0)
                    return NotificationSuppressionState.Restricted;
            }
            return sources.ShellState();
        }
        catch (Exception ex) when (IsQueryFailure(ex))
        {
            log($"Could not query Windows notification suppression; notify sound was suppressed: {ex.Message}");
            return NotificationSuppressionState.Failed;
        }
    }

    private static bool IsQueryFailure(Exception ex) =>
        ex is ExternalException or InvalidOperationException or NotSupportedException
            or DllNotFoundException or EntryPointNotFoundException;
}

public static class NotificationAudioPolicy
{
    public static bool UsesToastDefault(string? name) =>
        string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "beep", StringComparison.OrdinalIgnoreCase);

    public static bool PlayFromApp(string? name, bool toastShown) =>
        !string.IsNullOrEmpty(name) && (!toastShown || !UsesToastDefault(name));
}
