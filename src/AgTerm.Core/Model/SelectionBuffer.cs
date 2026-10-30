using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Model;

/// <summary>Where a mouse selection is copied (copyOnSelect, Windows-only). Selection keeps it in
/// the app's selection buffer for middle-click paste; Clipboard also writes the Windows clipboard;
/// Off keeps nothing. Ghostty's copy-on-select true, clipboard and false.</summary>
public enum CopyOnSelectMode { Selection, Clipboard, Off }

/// <summary>Reads copyOnSelect as a string. A JSON bool, as Windows Terminal writes its own
/// copyOnSelect, reads as true = selection and false = off, so it does not fail the whole file.</summary>
public sealed class CopyOnSelectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => "selection",
            JsonTokenType.False => "off",
            _ => throw new JsonException("copyOnSelect must be a string."),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>The app's selection buffer: one for every window, pane and the quick terminal, held in
/// memory only. It stands in for the selection clipboard upstream keeps in a private pasteboard
/// (agterm GhosttyCallbacks.swift pasteboard(for:)); Windows has no such clipboard.</summary>
public sealed class SelectionBuffer
{
    private string? text;

    public string? Text => Volatile.Read(ref text);

    /// <summary>Keeps a finished selection. Returns whether the Windows clipboard gets it too,
    /// which is only in clipboard mode (Ghostty Surface.zig setSelectionAndCopy).</summary>
    public bool Store(string selection, CopyOnSelectMode mode)
    {
        if (string.IsNullOrEmpty(selection) || mode == CopyOnSelectMode.Off) return false;
        Volatile.Write(ref text, selection);
        return mode == CopyOnSelectMode.Clipboard;
    }

    /// <summary>A middle-click paste. Clipboard mode reads the Windows clipboard, the other modes
    /// the buffer (Ghostty Surface.zig middle-press paste). Nothing to paste pastes nothing; a paste
    /// that could run a command asks first.</summary>
    public async Task PasteAsync(CopyOnSelectMode mode, bool bracketed, Func<Task<string?>> readClipboard,
        Func<string, Task<bool>> confirm, Func<string, Task> paste)
    {
        var source = mode == CopyOnSelectMode.Clipboard ? await readClipboard() : Text;
        if (string.IsNullOrEmpty(source)) return;
        if (!PasteProtection.IsSafe(source, bracketed) && !await confirm(source)) return;
        await paste(source);
    }
}

/// <summary>Ghostty's clipboard-paste-protection rule (input/paste.zig isSafeWith, Surface.zig
/// completeClipboardPaste). Bracketed, only the end of the frame is unsafe. Not bracketed, a line
/// break is unsafe too: a lone carriage return counts here as well, because it runs the line the
/// same way.</summary>
public static class PasteProtection
{
    private const string BracketedSuffix = "\u001b[201~";

    public static bool IsSafe(string text, bool bracketed) =>
        !text.Contains(BracketedSuffix, StringComparison.Ordinal) &&
        (bracketed || text.IndexOfAny(['\n', '\r']) < 0);
}
