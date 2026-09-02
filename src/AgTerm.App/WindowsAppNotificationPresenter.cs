using AgTerm.Xterm;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AgTerm.App;

internal sealed class WindowsAppNotificationPresenter : IDisposable
{
    private readonly Action<string>? _log;
    private readonly AppNotificationManager? _manager;

    public WindowsAppNotificationPresenter(Action<string>? log = null)
    {
        _log = log;
        try
        {
            var manager = AppNotificationManager.Default;
            manager.Register();
            _manager = manager;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Windows app notification registration failed: {ex.GetBaseException().Message}");
        }
    }

    public bool TryShow(LiveBannerNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (_manager is null)
            return false;

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("session", notification.SessionId.ToString("D"))
                .AddText(notification.Title)
                .AddText(notification.Body);
            if (notification.SoundName is null)
            {
                builder.MuteAudio();
            }
            else if (!WindowsNotificationSound.IsSupported(notification.SoundName))
            {
                builder.MuteAudio();
                _log?.Invoke(
                    $"Unsupported Windows notification sound '{notification.SoundName}' from notify; no sound was played.");
            }

            _manager.Show(builder.BuildNotification());
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Windows app notification failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_manager is null)
            return;

        try
        {
            _manager.Unregister();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Windows app notification unregister failed: {ex.GetBaseException().Message}");
        }
    }
}