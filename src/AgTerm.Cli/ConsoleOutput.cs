using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AgTerm.Cli;

/// <summary>
/// Gives stdout and stderr writers that lose no text (PLATFORM-NOTES). .NET's own Console.Out
/// encodes to the console output code page, and a page without the characters prints "?" — 437,
/// the default where the system locale is English, has no Cyrillic. A console takes UTF-16
/// through WriteConsoleW, so the console's code page, which the parent shell shares, stays as it
/// is. A redirected stream gets UTF-8 without a BOM, as upstream writes.
/// </summary>
internal static class ConsoleOutput
{
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    public static void Install()
    {
        if (!OperatingSystem.IsWindows()) return;
        Console.SetOut(ConsoleHandle(StdOutputHandle) is { } output
            ? new ConsoleWriter(output)
            : Utf8(Console.OpenStandardOutput()));
        Console.SetError(ConsoleHandle(StdErrorHandle) is { } error
            ? new ConsoleWriter(error)
            : Utf8(Console.OpenStandardError()));
    }

    private static StreamWriter Utf8(Stream stream) =>
        new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

    private static IntPtr? ConsoleHandle(int std)
    {
        var handle = GetStdHandle(std);
        // GetConsoleMode succeeds only for a console handle, never for a file or a pipe
        return handle != IntPtr.Zero && handle != new IntPtr(-1) && GetConsoleMode(handle, out _)
            ? handle
            : null;
    }

    private sealed class ConsoleWriter(IntPtr handle) : TextWriter
    {
        // a high surrogate written alone waits for its pair, so an emoji is never split
        private char? _high;

        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(char value)
        {
            if (char.IsHighSurrogate(value) && _high is null)
            {
                _high = value;
                return;
            }
            Write(_high is { } high ? new string([high, value]) : value.ToString());
        }

        public override void Write(char[] buffer, int index, int count) =>
            Write(new string(buffer, index, count));

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (_high is { } high)
            {
                _high = null;
                value = high + value;
            }
            var offset = 0;
            while (offset < value.Length)
            {
                if (!WriteConsoleW(handle, value[offset..], value.Length - offset, out var written, IntPtr.Zero))
                    throw new IOException(new Win32Exception(Marshal.GetLastPInvokeError()).Message);
                offset += written;
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int std);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WriteConsoleW(IntPtr handle, string buffer, int count, out int written, IntPtr reserved);
}
