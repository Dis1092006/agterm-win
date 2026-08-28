using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public static class SidebarAutomationText
{
    public static string Row(SidebarRow row, SidebarSurfaceStatus surfaceStatus)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        var parts = new List<string>
        {
            $"{Kind(row.Kind)} {row.Label}",
        };

        if (row.Kind == SidebarRowKind.Workspace)
        {
            parts.Add(row.Collapsed ? "collapsed" : "expanded");
            if (row.Focused)
            {
                parts.Add("focused");
            }

            var unseen = Unseen(row.Unseen);
            if (!string.IsNullOrEmpty(unseen))
            {
                parts.Add(unseen);
            }

            return string.Join(", ", parts);
        }

        if (row.Kind == SidebarRowKind.Session)
        {
            var surface = Surface(surfaceStatus);
            if (!string.IsNullOrEmpty(surface))
            {
                parts.Add(char.ToLowerInvariant(surface[0]) + surface[1..]);
            }

            if (row.Flagged)
            {
                parts.Add("flagged");
            }

            var agent = Agent(row.Indicator.Status);
            if (!string.IsNullOrEmpty(agent))
            {
                parts.Add(char.ToLowerInvariant(agent[0]) + agent[1..]);
            }

            var unseen = Unseen(row.Unseen);
            if (!string.IsNullOrEmpty(unseen))
            {
                parts.Add(unseen);
            }
        }

        return string.Join(", ", parts);
    }

    public static string Chevron(SidebarRow row)
    {
        if (row is null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        if (row.Kind != SidebarRowKind.Workspace)
        {
            return string.Empty;
        }

        return row.Collapsed
            ? $"Expand workspace {row.Label}"
            : $"Collapse workspace {row.Label}";
    }

    public static string Surface(SidebarSurfaceStatus surfaceStatus) => surfaceStatus switch
    {
        SidebarSurfaceStatus.Pending => "Terminal starting",
        SidebarSurfaceStatus.Failed => "Terminal failed to start",
        _ => string.Empty,
    };

    public static string Agent(AgentStatus status) => status switch
    {
        AgentStatus.Active => "Agent status active",
        AgentStatus.Completed => "Agent status completed",
        AgentStatus.Blocked => "Agent status blocked",
        _ => string.Empty,
    };

    public static string Unseen(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        return count == 1 ? "1 unseen notification" : $"{count} unseen notifications";
    }

    private static string Kind(SidebarRowKind kind) => kind switch
    {
        SidebarRowKind.Workspace => "Workspace",
        SidebarRowKind.Session => "Session",
        _ => kind.ToString(),
    };
}