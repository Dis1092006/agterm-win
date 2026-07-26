using System.Security.Cryptography;
using System.Text;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>
/// The rendezvous name of the control channel. Upstream binds a unix socket at
/// ControlResolve.socketPath (&lt;state dir&gt;\agterm.sock); Windows named pipes live in the flat
/// \\.\pipe\ namespace, so the SAME socket path is derived to a deterministic pipe name instead
/// (see PLATFORM-NOTES). AGTERM_CONTROL_SOCKET stays the explicit override, holding the literal
/// pipe name (no \\.\pipe\ prefix).
/// </summary>
public static class ControlPipeName
{
    /// <summary>The pipe name for the ambient environment: the AGTERM_CONTROL_SOCKET override
    /// verbatim, else the name derived from the AGTERM_STATE_DIR socket path.</summary>
    public static string Resolve(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        if (getEnv("AGTERM_CONTROL_SOCKET") is { Length: > 0 } explicitName) return explicitName;
        var socketPath = ControlResolve.SocketPath(
            stateDir: getEnv("AGTERM_STATE_DIR"),
            appSupport: PersistenceStore.DefaultDirectory);
        return Derive(socketPath);
    }

    /// <summary>Derives the pipe name from the platform-neutral socket path: a stable hash keeps the
    /// per-state-dir isolation of the unix socket path while fitting the flat pipe namespace.</summary>
    public static string Derive(string socketPath)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(socketPath));
        return "agterm-" + Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }
}
