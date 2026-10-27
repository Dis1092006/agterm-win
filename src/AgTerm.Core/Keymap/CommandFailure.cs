using System.Globalization;
using System.Text;
using AgTerm.Core.Model;

namespace AgTerm.Core.Keymap;

/// <summary>Shapes a failed custom command's diagnostics into the two strings a HUD can paint. Port of
/// upstream CommandFailure. The runner opens the HUD directly, which skips the dispatcher's text
/// validation, so every value here is already stripped of escape sequences and capped at
/// <see cref="HudSpec.MaxTextLength"/>.</summary>
public static class CommandFailure
{
    /// <summary>A diagnostic bound, not a measured one: only the last line is shown, and a command that
    /// writes megabytes must not grow the buffer with it.</summary>
    public const int TailLimit = 16 * 1024;

    private const string Separator = ": ";

    /// <summary>Text longer than this is cut at a text element before the exact cap runs. NFC composes
    /// several UTF-16 units into one, but not eight to one, so what is cut here would not have fit anyway;
    /// the exact loop then only trims what is left, instead of re-measuring a whole tail per element.</summary>
    private const int CutBeforeCapping = 8 * HudSpec.MaxTextLength;

    private const int MaxLeadingContinuationBytes = 3;

    /// <summary>The last usable line of a captured tail. Lines are cleaned BEFORE one is chosen: a command
    /// whose last write was a bare colour reset would otherwise show an empty panel instead of the
    /// diagnostic above it.</summary>
    public static string? Detail(ReadOnlySpan<byte> tail)
    {
        var lines = Decoded(tail).Split(['\r', '\n']);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var clean = Cleaned(lines[i]);
            if (clean.Length > 0) return Capped(clean);
        }
        return null;
    }

    /// <summary>A long name is truncated to keep the reason, which is the half that says what happened.</summary>
    public static string Message(string name, string reason)
    {
        reason = Cleaned(reason);
        var room = HudSpec.MaxTextLength - HudLayout.TextLength(reason + Separator);
        if (room <= 0) return Capped(reason);
        var cleanName = Bounded(Cleaned(name));
        while (HudLayout.TextLength(cleanName) > room && cleanName.Length > 0)
            cleanName = WithoutLastTextElement(cleanName);
        return cleanName.Length == 0 ? reason : cleanName + Separator + reason;
    }

    /// <summary>The HUD helper prints these bytes into a live terminal, where an unstripped CSI would paint
    /// outside the panel. <see cref="TerminalText.Sanitized"/> removes the C0 range but leaves the
    /// <c>[31m</c> an escape sequence trails behind, so the sequences go first.</summary>
    private static string Cleaned(string text) =>
        TerminalText.Sanitized(StrippingEscapes(text)).Trim();

    /// <summary>A trailing partial sequence is dropped with it, since a cut tail can end mid-escape.</summary>
    private static string StrippingEscapes(string text)
    {
        if (!text.Contains('\u001b')) return text;
        var result = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] != '\u001b')
            {
                result.Append(text[index++]);
                continue;
            }
            index++;
            if (index >= text.Length) break;
            var kind = text[index++];
            if (kind == '[')
            {
                while (index < text.Length && text[index] is < '\u0040' or > '\u007e') index++;
                index++;
            }
            else if (kind == ']')
            {
                while (index < text.Length && text[index] != '\u0007' && text[index] != '\u001b') index++;
                if (index < text.Length && text[index] == '\u001b') index++;
                index++;
            }
        }
        return result.ToString();
    }

    /// <summary>Measured in <see cref="HudLayout.TextLength"/>'s unit, so a multi-scalar grapheme costs more
    /// than one character. Whole text elements go, so the cut never leaves half a grapheme behind.</summary>
    private static string Capped(string text)
    {
        text = Bounded(text);
        while (HudLayout.TextLength(text) > HudSpec.MaxTextLength && text.Length > 0)
            text = WithoutLastTextElement(text);
        return text;
    }

    private static string Bounded(string text)
    {
        if (text.Length <= CutBeforeCapping) return text;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var cut = starts.Last(start => start <= CutBeforeCapping);
        return text[..cut];
    }

    /// <summary>UTF-8 when the bytes are valid, else the console's OEM code page: cmd.exe and many native
    /// tools write it, and a localized error would otherwise turn into replacement characters. A cut tail can
    /// start inside a UTF-8 sequence, so leading continuation bytes are dropped before the validity check.</summary>
    private static string Decoded(ReadOnlySpan<byte> tail)
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        if (TryDecode(strict, tail, out var text)) return text;
        var skipped = 0;
        while (skipped < MaxLeadingContinuationBytes && skipped < tail.Length && (tail[skipped] & 0xC0) == 0x80)
            skipped++;
        if (skipped > 0 && TryDecode(strict, tail[skipped..], out text)) return text;
        return OemEncoding()?.GetString(tail) ?? Encoding.UTF8.GetString(tail);
    }

    private static bool TryDecode(Encoding strict, ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }

    private static Encoding? OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string WithoutLastTextElement(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        return text[..starts[^1]];
    }
}
