using System.Globalization;

namespace AgTerm.Core.Model;

/// <summary>Window naming rules — the port of agtermCore WindowLibrary.isAutoName.</summary>
public static class WindowNames
{
    /// <summary>Whether a name matches the auto-assigned scheme: the literal word "window" followed by
    /// a positive integer ("window 1", "window 2", …). Any other name was set by the user.</summary>
    public static bool IsAutoName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is ["window", var number]
            && long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            && n >= 1;
    }
}
