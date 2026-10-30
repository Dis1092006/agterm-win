using AgTerm.Xterm;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AgTerm.App;

internal sealed class WindowsAppNotificationPresenter : IDisposable
{
    private readonly Action<string>? _log;
    private readonly AppNotificationManager? _manager;
    private readonly Action<LiveNotificationTarget?>? _activated;
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);

    public WindowsAppNotificationPresenter(
        Action<string>? log = null, Action<LiveNotificationTarget?>? activated = null)
    {
        _log = log;
        _activated = activated;
        AppNotificationManager? manager = null;
        try
        {
            manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            _manager = manager;
        }
        catch (Exception ex)
        {
            if (manager is not null) manager.NotificationInvoked -= OnNotificationInvoked;
            _log?.Invoke($"Windows app notification registration failed: {ex.GetBaseException().Message}");
        }
    }

    public async Task<bool> TryShowAsync(LiveBannerNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (_manager is null)
            return false;

        await _deliveryGate.WaitAsync();
        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("session", notification.SessionId.ToString("D"))
                .AddText(notification.Title)
                .AddText(notification.Body);
            if (NotificationAudioPolicy.UsesToastDefault(notification.SoundName))
                builder.SetAudioEvent(AppNotificationSoundEvent.Default);
            else
                builder.MuteAudio();

            if (notification.Target is { } target)
                builder.AddArgument("target", target.Identity);
            var toast = builder.BuildNotification();
            if (notification.Target is { } identity)
            {
                toast.Tag = identity.ToastTag;
                toast.Group = identity.ToastGroup;
            }
            _manager.Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Windows app notification failed: {ex.GetBaseException().Message}");
            return false;
        }
        finally { _deliveryGate.Release(); }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        HandleActivation(args);

    internal void HandleActivation(AppNotificationActivatedEventArgs args)
    {
        var target = args.Arguments.TryGetValue("target", out var identity)
            ? LiveNotificationTarget.Parse(identity) : null;
        if (target is null)
            _log?.Invoke("Notification activation has no valid pane target; activating the app.");
        _activated?.Invoke(target);
    }

    public async Task ClearSessionAsync(Guid sessionId)
    {
        if (_manager is null) return;
        await _deliveryGate.WaitAsync();
        try
        {
            await _manager.RemoveByGroupAsync(sessionId.ToString("D").ToUpperInvariant());
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Windows app notification removal failed: {ex.GetBaseException().Message}");
        }
        finally { _deliveryGate.Release(); }
    }

    public void Dispose()
    {
        if (_manager is null)
            return;

        try
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            _manager.Unregister();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Windows app notification unregister failed: {ex.GetBaseException().Message}");
        }
    }
}