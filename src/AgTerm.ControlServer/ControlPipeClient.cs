using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>A failure talking to the control pipe (connect/write/read/decode), distinct from a
/// server-side {"ok":false} response (which is a valid decoded ControlResponse).</summary>
public sealed class PipeClientException : Exception
{
    public PipeClientException(string message) : base(message)
    {
    }
}

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
    public ControlResponse Send(ControlRequest request)
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
            return ControlJson.Deserialize<ControlResponse>(line)
                ?? throw new PipeClientException("could not decode response: empty response");
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
            throw new PipeClientException($"connect({_pipeName}) failed: {e.Message} — is agterm running?");
        }
    }

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
