namespace AgTerm.App;

/// <summary>Which embedded icon the window shows, by the color of the taskbar.</summary>
public static class TaskbarAppIcon
{
    public const string ForLightTaskbar = "AppIcon.ico";
    public const string ForDarkTaskbar = "AppIcon-dark-taskbar.ico";

    /// <summary>The taskbar follows the Windows mode, <c>SystemUsesLightTheme</c> under
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>, not the app mode that
    /// <c>UISettings</c> reports; the two can differ. A missing value is a dark taskbar: Windows 10 1809,
    /// the oldest supported version, has neither a light taskbar nor the value.</summary>
    public static string ResourceFor(object? systemUsesLightTheme) =>
        systemUsesLightTheme is 1 ? ForLightTaskbar : ForDarkTaskbar;
}
