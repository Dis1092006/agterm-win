using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>
/// The programmatic control channel: a named-pipe listener that turns newline-delimited JSON
/// ControlRequests into calls on the host's dispatch seam. One request per connection: read a line,
/// dispatch, write one ControlResponse, close — the Windows port of the upstream unix-socket
/// ControlServer accept loop (§5). Serial by design: connections are rare and short.
///
/// Best-effort lifecycle: a bind failure logs and the host still starts. The dispatch delegate runs
/// on the accept-loop thread; a host that confines its store to one context must marshal inside the
/// delegate (the headless host serializes with a lock; see PLATFORM-NOTES).
/// </summary>
public sealed class ControlPipeServer : IDisposable
{
    /// <summary>1 MiB cap on a single request line — far above any realistic session.type payload.
    /// A line that exceeds it is rejected and the connection closed.</summary>
    public const int MaxLineBytes = 1 << 20;

    /// <summary>Seconds a single client read may stall before the connection is abandoned.</summary>
    public const int ReadTimeoutSeconds = 5;

    /// <summary>Overall seconds a single connection's request read may take — a slow-loris client
    /// trickling bytes under the per-read timeout must not park the serial accept loop.</summary>
    public const int ReadDeadlineSeconds = 10;

    /// <summary>Overall seconds a single response write may take, symmetric with the read deadline.</summary>
    public const int WriteDeadlineSeconds = 10;

    private readonly string _pipeName;
    private readonly Func<ControlRequest, Task<ControlResponse?>>? _dispatch;
    private readonly Func<ControlRequest, CancellationToken, Task<ControlResponse?>>?
        _dispatchWithCancellation;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public ControlPipeServer(string pipeName, Func<ControlRequest, Task<ControlResponse?>> dispatch,
                             Action<string>? log = null)
    {
        _pipeName = pipeName;
        _dispatch = dispatch;
        _log = log;
    }

    public ControlPipeServer(
        string pipeName,
        Func<ControlRequest, CancellationToken, Task<ControlResponse?>> dispatch,
        Action<string>? log = null)
    {
        _pipeName = pipeName;
        _dispatchWithCancellation = dispatch;
        _log = log;
    }

    public string PipeName => _pipeName;

    /// <summary>Whether the accept loop is running (the pipe is being served).</summary>
    public bool IsListening => _acceptLoop is not null;

    /// <summary>Bind and start the serial accept loop. Idempotent: a no-op if already listening.
    /// On failure it logs and returns, leaving the host to start normally.</summary>
    public void Start()
    {
        if (_acceptLoop is not null) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _acceptLoop = Task.Run(() => AcceptLoop(cts.Token), CancellationToken.None);
    }

    /// <summary>Stop the listener. A connection currently being served finishes best-effort.</summary>
    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        _acceptLoop = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = Create();
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                await HandleConnection(pipe, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"control pipe accept failed: {e.Message}");
                // brief backoff so a persistent bind failure can't spin the loop hot.
                try
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    /// <summary>One instance at a time — the accept loop is serial like the upstream socket server;
    /// a second concurrent client waits in ConnectAsync until the current connection closes.
    /// CurrentUserOnly is the named-pipe analogue of the socket file's owner-only 0600 chmod.</summary>
    private NamedPipeServerStream Create() =>
        new(_pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task HandleConnection(NamedPipeServerStream pipe, CancellationToken token)
    {
        var line = await ReadLine(pipe).ConfigureAwait(false);
        if (line is null)
        {
            await WriteResponse(pipe,
                new ControlResponse { Ok = false, Error = "request too large or read failed" })
                .ConfigureAwait(false);
            return;
        }

        ControlRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ControlRequest>(line, ControlJson.Options);
        }
        catch (JsonException e)
        {
            await WriteResponse(pipe,
                new ControlResponse { Ok = false, Error = $"invalid request: {e.Message}" })
                .ConfigureAwait(false);
            return;
        }
        if (request is null)
        {
            await WriteResponse(pipe,
                new ControlResponse { Ok = false, Error = "invalid request: empty request" })
                .ConfigureAwait(false);
            return;
        }

        ControlResponse response;
        try
        {
            // a null dispatcher result is the one declined command (debug.appearance, the UI-test-only
            // seam upstream handles app-side); answer with the pinned upstream refusal.
            var dispatched = _dispatchWithCancellation is not null
                ? await _dispatchWithCancellation(request, token).ConfigureAwait(false)
                : await _dispatch!(request).ConfigureAwait(false);
            response = dispatched
                ?? new ControlResponse { Ok = false, Error = "debug.appearance is a UI-test-only seam" };
        }
        catch (Exception e)
        {
            // never let a host fault tear the accept loop down mid-request — §5's "never a crash".
            response = new ControlResponse { Ok = false, Error = $"internal error: {e.Message}" };
        }
        await WriteResponse(pipe, response).ConfigureAwait(false);
    }

    /// <summary>Read bytes up to (and excluding) the first newline, capping at MaxLineBytes, with a
    /// per-read timeout and an overall deadline. Null on EOF-before-newline (empty), read error,
    /// the size cap, or the deadline.</summary>
    private static async Task<byte[]?> ReadLine(PipeStream pipe)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(ReadDeadlineSeconds);
        while (true)
        {
            if (DateTime.UtcNow > deadline) return null;
            int n;
            using (var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(ReadTimeoutSeconds)))
            {
                try
                {
                    n = await pipe.ReadAsync(chunk, readCts.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException or IOException)
                {
                    return null;
                }
            }
            if (n == 0) return buffer.Length > 0 ? buffer.ToArray() : null; // EOF: accept a trailing line
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, n);
            if (newline >= 0)
            {
                buffer.Write(chunk, 0, newline);
                // the cap bounds the line CONTENT, newline or not — matching the upstream
                // byte-by-byte reader, which rejects before it ever sees the terminator.
                return buffer.Length > MaxLineBytes ? null : buffer.ToArray();
            }
            buffer.Write(chunk, 0, n);
            if (buffer.Length > MaxLineBytes) return null;
        }
    }

    /// <summary>Encode the response and write it back as a single newline-terminated line, bounded
    /// by the write deadline. A client that hung up is a normal close, never a fault.</summary>
    private static async Task WriteResponse(PipeStream pipe, ControlResponse response)
    {
        var data = Encoding.UTF8.GetBytes(ControlJson.Serialize(response) + "\n");
        using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(WriteDeadlineSeconds));
        try
        {
            await pipe.WriteAsync(data, writeCts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(writeCts.Token).ConfigureAwait(false);
            // let the client drain the response before the server side tears the pipe down.
            if (OperatingSystem.IsWindows()) pipe.WaitForPipeDrain();
        }
        catch (Exception e) when (e is OperationCanceledException or IOException
                                      or ObjectDisposedException or InvalidOperationException)
        {
        }
    }
}
