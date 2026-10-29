using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;

namespace AgTerm.App;

public sealed class WindowsNotificationSuppressionSources : INotificationSuppressionSources
{
    public bool NotificationsEnabled() => AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;

    public bool HasNotificationMode() => ApiInformation.IsPropertyPresent(
        "Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationMode");

    public int NotificationMode()
    {
        if (ApiInformation.IsPropertyPresent("Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationMode"))
            return (int)ToastNotificationManager.GetDefault().NotificationMode;
        throw new NotSupportedException("Windows notification mode is unavailable.");
    }

    public uint? QuietHoursProfile()
    {
        // Undocumented fallback for Windows versions without NotificationMode. Read failure is fail-open.
        ulong name = 0x0D83063EA3BF1C75;
        var buffer = new byte[4];
        uint size = 4;
        var status = NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref size);
        return status == 0 && size == 4 ? BitConverter.ToUInt32(buffer, 0) : null;
    }

    public NotificationSuppressionState ShellState() => new ShellNotificationSuppression().Query();

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope,
        out uint changeStamp, [Out] byte[] buffer, ref uint bufferSize);
}
