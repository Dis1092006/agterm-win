using System.IO.Pipes;
using System.Text;

namespace AgTerm.Cli.Tests;

/// <summary>A one-shot pipe server that answers with <c>line</c> exactly, so a test can send bytes
/// no encoder would produce. The port of upstream's <c>StubServer(line:)</c>.</summary>
internal static class RawLineServer
{
    public static Task Start(string pipe, string line)
    {
        var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        return Task.Run(async () =>
        {
            await using (server)
            {
                await server.WaitForConnectionAsync();
                var request = new byte[1];
                while (await server.ReadAsync(request) == 1 && request[0] != (byte)'\n')
                {
                }
                await server.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
                await server.FlushAsync();
                // wait for the client to hang up, so the line is read before the pipe closes.
                while (await server.ReadAsync(request) > 0)
                {
                }
            }
        });
    }
}
