using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>A failure talking to the control pipe (connect/write/read/decode), distinct from a
/// server-side {"ok":false} response (which is a valid decoded ControlResponse).</summary>
public sealed class PipeClientException : Exception
{
    public PipeClientException(string message, PipeConnectFailure? connectFailure = null) : base(message)
    {
        ConnectFailure = connectFailure;
    }

    /// <summary>Why the connect failed, null when the failure came after the connect.</summary>
    public PipeConnectFailure? ConnectFailure { get; }
}

/// <summary>How a connect to the control pipe failed. The pipe is single-instance, so a name that
/// exists but takes no client means a present owner that is not accepting.</summary>
public enum PipeConnectFailure
{
    /// <summary>The pipe exists and every instance is taken: the owner is there.</summary>
    Busy,

    /// <summary>No pipe by that name: the owner is gone, or between two connections.</summary>
    Missing,

    /// <summary>The probe could not tell.</summary>
    Unknown,

    /// <summary>Any other connect error, such as access denied.</summary>
    Other,
}

/// <summary>A decoded response together with the line it arrived as (without the newline). The
/// CLI reads the response and prints the line under <c>--json</c>, so a field this build does not
/// model still reaches the caller. The port of upstream's <c>SocketReply</c>.</summary>
public sealed record ControlReply(ControlResponse Response, string Line);

/// <summary>
/// A blocking, one-request-per-connection client for the agterm control pipe: connect, write the
/// request line, read the single response line, decode it — the Windows port of the upstream
/// SocketClient.
/// </summary>
public sealed class ControlPipeClient
{
    /// <summary>64 MiB cap on a response line. Requests stay small; a session.text --all response
    /// carries the whole scrollback and can reach several MiB. It comes from our own server, so the
    /// cap only guards against a runaway read.</summary>
    public const int MaxLineBytes = 64 << 20;

    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;

    public ControlPipeClient(string pipeName, int connectTimeoutMs = 5000)
    {
        _pipeName = pipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    public string PipeName => _pipeName;

    /// <summary>Connect, send the request as one newline-terminated JSON line, read the response
    /// line, decode it.</summary>
    public ControlResponse Send(ControlRequest request) => SendReply(request).Response;

    /// <summary><see cref="Send"/>, also keeping the response line as the server wrote it, so
    /// <c>--json</c> can print it unchanged.</summary>
    public ControlReply SendReply(ControlRequest request)
    {
        using var pipe = Connect();

        var data = Encoding.UTF8.GetBytes(ControlJson.Serialize(request) + "\n");
        try
        {
            pipe.Write(data);
            pipe.Flush();
        }
        catch (IOException e)
        {
            throw new PipeClientException($"write failed: {e.Message}");
        }

        var line = ReadLine(pipe);
        if (line is null)
        {
            throw new PipeClientException($"no response from {_pipeName}");
        }
        try
        {
            var response = ControlJson.Deserialize<ControlResponse>(line)
                ?? throw new PipeClientException("could not decode response: empty response");
            return new ControlReply(response, Encoding.UTF8.GetString(line));
        }
        catch (JsonException e)
        {
            throw new PipeClientException($"could not decode response: {e.Message}");
        }
    }

    private NamedPipeClientStream Connect()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.None);
        try
        {
            pipe.Connect(_connectTimeoutMs);
            return pipe;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            var failure = e is TimeoutException ? ProbeTimedOutPipe(_pipeName) : PipeConnectFailure.Other;
            throw new PipeClientException($"connect({_pipeName}) failed: {e.Message} — {Hint(failure)}", failure);
        }
    }

    /// <summary>The sentence after a failed connect, worded as upstream's (#633). Only a pipe seen
    /// busy says the owner is there; a missing name or an unanswered probe stays uncertain.</summary>
    private static string Hint(PipeConnectFailure failure) => failure switch
    {
        PipeConnectFailure.Busy => "the socket owner is present but not accepting connections",
        PipeConnectFailure.Missing or PipeConnectFailure.Unknown => "agterm may be stopped or unable to accept connections",
        _ => "is agterm running?",
    };

    /// <summary>Why a connect timed out. .NET waits the whole timeout both for a busy pipe and for a
    /// missing one, so ask once more: WaitNamedPipe fails with ERROR_SEM_TIMEOUT when the name exists
    /// and every instance is taken, and with ERROR_FILE_NOT_FOUND when no instance exists.</summary>
    private static PipeConnectFailure ProbeTimedOutPipe(string pipeName)
    {
        if (!OperatingSystem.IsWindows()) return PipeConnectFailure.Unknown;
        if (WaitNamedPipe($@"\\.\pipe\{pipeName}", 1)) return PipeConnectFailure.Busy;
        return Marshal.GetLastPInvokeError() switch
        {
            ErrorSemTimeout => PipeConnectFailure.Busy,
            ErrorFileNotFound => PipeConnectFailure.Missing,
            _ => PipeConnectFailure.Unknown,
        };
    }

    private const int ErrorFileNotFound = 2;
    private const int ErrorSemTimeout = 121;

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipe(string name, uint timeoutMs);

    /// <summary>Read up to (and excluding) the first newline, capping at MaxLineBytes. Null on
    /// EOF-before-newline (empty), error, or cap exceeded. Reads in 64 KiB chunks so a multi-MB
    /// session.text --all response takes a handful of syscalls instead of one per byte.</summary>
    private static byte[]? ReadLine(PipeStream pipe)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            int n;
            try
            {
                n = pipe.Read(chunk);
            }
            catch (IOException)
            {
                return null;
            }
            if (n == 0) return buffer.Length > 0 ? buffer.ToArray() : null;
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, n);
            if (newline >= 0)
            {
                buffer.Write(chunk, 0, newline);
                return buffer.ToArray();
            }
            buffer.Write(chunk, 0, n);
            if (buffer.Length > MaxLineBytes) return null;
        }
    }
}
