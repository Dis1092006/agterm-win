using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public static class SidebarBadge
{
    public static bool IsShown(int unseen, AppSettings settings) =>
        settings.NotificationBadgeEnabled != false && unseen > 0;
}
