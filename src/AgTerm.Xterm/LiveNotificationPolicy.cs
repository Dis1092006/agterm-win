using AgTerm.ControlServer;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed record LiveBannerNotification(
    Guid SessionId,
    string Title,
    string Body,
    string? SoundName);

public sealed class SoundThrottle
{
    private readonly TimeSpan _window;
    private readonly Dictionary<string, TimeSpan> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);

    public SoundThrottle(TimeSpan window)
    {
        if (window < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));
        _window = window;
    }

    public bool Allow(string name, TimeSpan now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_lastPlayed.TryGetValue(name, out var last) && now - last < _window)
            return false;

        _lastPlayed[name] = now;
        return true;
    }
}

public static class LiveNotificationPolicy
{
    public static LiveBannerNotification? ForNotify(
        LiveNotificationRequest request,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.NotificationsEnabled == false
            ? null
            : new LiveBannerNotification(
                request.SessionId,
                request.Title,
                request.Body,
                Normalize(settings.NotificationSoundName));
    }

    public static string? StatusSound(
        LiveStatusNotificationRequest request,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        return Normalize(request.Status.EffectiveSound(
            request.SoundName,
            settings.BlockedStatusSoundName));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}