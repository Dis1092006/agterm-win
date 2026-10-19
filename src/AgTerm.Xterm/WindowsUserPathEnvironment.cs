using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AgTerm.Core.Install;
using Microsoft.Win32;

namespace AgTerm.Xterm;

/// <summary>The user <c>Path</c> in <c>HKCU\Environment</c> and this process's <c>PATH</c>. Reads the
/// stored value without expanding <c>%VARIABLES%</c> and writes it back with the kind it had, so a
/// <c>REG_EXPAND_SZ</c> value stays <c>REG_EXPAND_SZ</c>. The path logic itself is <see cref="CliInstall"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUserPathEnvironment : IUserPathEnvironment
{
    // registry value names are case-insensitive: "Path" also reads and updates a value stored as "PATH"
    private const string ValueName = "Path";

    private readonly Func<RegistryKey> _openKey;
    private readonly Action _broadcast;

    /// <param name="openKey">Opens the writable key that holds the value; disposed after each use.</param>
    /// <param name="broadcast">Tells running applications that the environment changed.</param>
    public WindowsUserPathEnvironment(Func<RegistryKey> openKey, Action broadcast)
    {
        _openKey = openKey;
        _broadcast = broadcast;
    }

    /// <summary>The real <c>HKCU\Environment</c> with the <c>WM_SETTINGCHANGE</c> broadcast.</summary>
    public static WindowsUserPathEnvironment CreateDefault() =>
        new(() => Registry.CurrentUser.CreateSubKey("Environment", writable: true), BroadcastEnvironmentChange);

    public UserPathValue? ReadUserPath()
    {
        using var key = _openKey();
        if (key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } raw)
            return null;
        var kind = key.GetValueKind(ValueName);
        if (raw is not string value || kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new InvalidOperationException($"The user Path in the registry is not a string value ({kind}).");
        return new UserPathValue(value, kind == RegistryValueKind.ExpandString);
    }

    public void WriteUserPath(UserPathValue value)
    {
        using var key = _openKey();
        key.SetValue(ValueName, value.Value, value.Expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    public void BroadcastChange() => _broadcast();

    public string? ReadProcessPath() => Environment.GetEnvironmentVariable("PATH");

    public void WriteProcessPath(string value) => Environment.SetEnvironmentVariable("PATH", value);

    public string ExpandVariables(string value) => Environment.ExpandEnvironmentVariables(value);

    private static void BroadcastEnvironmentChange() =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 5000, out _);

    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
