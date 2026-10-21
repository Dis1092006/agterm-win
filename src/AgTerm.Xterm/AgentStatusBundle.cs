using System.Reflection;

namespace AgTerm.Xterm;

public static class AgentStatusBundle
{
    public static string Read(string filename)
    {
        using var stream = typeof(AgentStatusBundle).Assembly.GetManifestResourceStream($"agent-status/{filename}")
            ?? throw new FileNotFoundException($"The agent-status script '{filename}' is not bundled in this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
