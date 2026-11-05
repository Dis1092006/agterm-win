using System.CommandLine;
using System.CommandLine.Parsing;using System.Runtime.InteropServices;
using System.Text;

namespace AgTerm.Cli;

// Port of agtermctlKit ClipboardCommands.swift and agtermCore TerminalClipboard.swift (upstream
// fbe72e25, #722). Upstream prints an OSC 52 write into the caller's pane through `zmx print`, so the
// text reaches the Mac showing the pane. agterm-win has no zmx and no remote attach, so the text goes
// to the Windows clipboard of the machine the CLI runs on, which is the one the user sits at. Like
// upstream it is CLI-only: no wire command, no --json, no --target.

/// <summary>A command that never touches the control pipe, so it has no request and no connection
/// options.</summary>
internal interface ILocalCommand
{
    void Validate()
    {
    }

    int Execute(CliEnvironment env);
}

internal static class TerminalClipboard
{
    /// <summary>Upstream's limit on the base64 form of the text, kept so the same text is refused on
    /// both platforms.</summary>
    public const int MaxEncodedBytes = 8 * 1024 * 1024 - 64;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal enum FailureKind { EmptyText, TooLarge, InvalidUtf8, Unavailable }

    internal readonly record struct Failure(FailureKind Kind, long Value = 0)
    {
        public static Failure EmptyText => new(FailureKind.EmptyText);

        public static Failure InvalidUtf8 => new(FailureKind.InvalidUtf8);

        public static Failure TooLarge(long encodedBytes) => new(FailureKind.TooLarge, encodedBytes);

        public static Failure Unavailable(int errorCode) => new(FailureKind.Unavailable, errorCode);

        public string Message => Kind switch
        {
            FailureKind.EmptyText => "nothing to copy",
            FailureKind.TooLarge =>
                $"text is too large to copy through the terminal: {Value} bytes encoded, the limit is {MaxEncodedBytes}",
            FailureKind.InvalidUtf8 => "text is not valid UTF-8; the Windows clipboard holds Unicode text",
            _ => $"could not set the Windows clipboard: error {Value}",
        };
    }

    internal sealed class FailureException : Exception
    {
        public FailureException(Failure failure) : base(failure.Message) => Failure = failure;

        public Failure Failure { get; }
    }

    /// <summary>The text to put on the clipboard, or a FailureException when the bytes cannot be copied.</summary>
    public static string Prepare(byte[] text)
    {
        if (text.Length == 0) throw new FailureException(Failure.EmptyText);
        var encoded = (text.LongLength + 2) / 3 * 4;
        if (encoded > MaxEncodedBytes) throw new FailureException(Failure.TooLarge(encoded));
        try
        {
            return Strict.GetString(text);
        }
        catch (DecoderFallbackException)
        {
            throw new FailureException(Failure.InvalidUtf8);
        }
    }
}

/// <summary>CF_UNICODETEXT through the Win32 clipboard. A process without a window opens the clipboard
/// with a null owner window, which works from an agent's shell tool.</summary>
internal static class WindowsClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int OpenAttempts = 10;

    public static void SetText(string text)
    {
        OpenWithRetry();
        try
        {
            if (!EmptyClipboard()) throw Unavailable();
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero) throw Unavailable();
            var locked = GlobalLock(handle);
            if (locked == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                GlobalFree(handle);
                throw new TerminalClipboard.FailureException(TerminalClipboard.Failure.Unavailable(error));
            }
            Marshal.Copy(bytes, 0, locked, bytes.Length);
            GlobalUnlock(handle);
            if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                GlobalFree(handle);
                throw new TerminalClipboard.FailureException(TerminalClipboard.Failure.Unavailable(error));
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    // another process may hold the clipboard for a moment
    private static void OpenWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return;
            if (attempt == OpenAttempts) throw Unavailable();
            Thread.Sleep(20);
        }
    }

    private static TerminalClipboard.FailureException Unavailable() =>
        new(TerminalClipboard.Failure.Unavailable(Marshal.GetLastWin32Error()));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}

internal static class ClipboardCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var group = new System.CommandLine.Command("clipboard", "The clipboard of the machine this CLI runs on.");
        var set = new System.CommandLine.Command("set", "Copy text to the clipboard.");
        var text = new Argument<string?>("text")
        {
            Description = "The text to copy. Read from standard input when omitted. Put -- before text that starts with a dash.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        set.Arguments.Add(text);
        bindings.RegisterLocal(set, result => new Set
        {
            Text = result.GetValue(text),
            AfterTerminator = result.Tokens.Any(token => token.Type == TokenType.DoubleDash),
        });
        group.Subcommands.Add(set);
        return group;
    }

    internal sealed class Set : ILocalCommand
    {
        public string? Text { get; init; }

        /// <summary>Whether `--` came before the text; without it a dash-leading text is an unknown
        /// option, as in ArgumentParser.</summary>
        public bool AfterTerminator { get; init; }

        public void Validate()
        {
            if (!AfterTerminator && Text is { Length: > 1 } text && text[0] == '-')
                throw new CliValidationException($"Unknown option '{text}'");
        }

        public Func<Stream> StdinStream { get; init; } = Console.OpenStandardInput;

        public Func<bool> StdinIsTerminal { get; init; } = () => !Console.IsInputRedirected;

        public int Execute(CliEnvironment env)
        {
            byte[] data;
            if (Text is { } text)
            {
                data = Encoding.UTF8.GetBytes(text);
            }
            else
            {
                // a usage error like upstream's ValidationError from run(): exit 64, not 1
                if (StdinIsTerminal())
                {
                    env.Error.WriteLine("error: give the text as an argument or pipe it on standard input");
                    return 64;
                }
                data = TypedStdin.ReadAll(StdinStream());
            }
            try
            {
                env.SetClipboard(TerminalClipboard.Prepare(data));
            }
            catch (TerminalClipboard.FailureException failure)
            {
                throw new CliValidationException(failure.Message);
            }
            return 0;
        }
    }
}
