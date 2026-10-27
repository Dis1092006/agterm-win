using System.Runtime.InteropServices;
using System.Text;

namespace AgTerm.Terminal;

/// <summary>The CREATE_UNICODE_ENVIRONMENT block CreateProcessW wants, shared by every spawner here.</summary>
internal static class EnvironmentBlock
{
    /// <summary>The inherited process environment with the removals applied and the extras overlaid
    /// (case-insensitive keys, like the environment itself). Zero when there is nothing to change
    /// (null = plain inheritance, cheaper and identical).</summary>
    internal static IntPtr Build(IReadOnlyDictionary<string, string>? extra, IReadOnlyList<string>? remove)
    {
        if ((extra is null || extra.Count == 0) && (remove is null || remove.Count == 0))
            return IntPtr.Zero;
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            merged[(string)entry.Key] = (string?)entry.Value ?? "";
        // removal first: an explicit extra of the same name is a declaration, and wins
        if (remove is not null)
            foreach (var key in remove)
                merged.Remove(key);
        if (extra is not null)
            foreach (var (key, value) in extra)
                merged[key] = value;
        return Encode(merged);
    }

    /// <summary>Sorted per the CreateProcessW contract, double-NUL-terminated. The caller frees the
    /// block with <see cref="Marshal.FreeHGlobal"/>.</summary>
    internal static IntPtr Encode(IReadOnlyDictionary<string, string> variables)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in variables)
            sorted[key] = value;
        var block = new StringBuilder();
        foreach (var (key, value) in sorted)
            block.Append(key).Append('=').Append(value).Append('\0');
        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }
}
