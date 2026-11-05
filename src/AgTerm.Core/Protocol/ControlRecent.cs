using System.Text.Json.Serialization;
using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

/// <summary>One Recent Closed entry as recent.list reports it: a summary, never the snapshot it restores
/// from. WorkspaceId is the parent workspace for a session entry and the workspace itself for a
/// workspace entry; SessionIds are the sessions a reopen brings back. ClosedAt is Unix seconds.</summary>
public sealed record ControlRecentItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("closedAt")]
    public required double ClosedAt { get; init; }

    [JsonPropertyName("workspaceID")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("sessionIDs")]
    public required string[] SessionIds { get; init; }

    public static ControlRecentItem From(RecentClosedItem item)
    {
        Guid? workspaceId;
        Guid[] sessionIds;
        if (item.Kind == RecentClosedKind.Session)
        {
            workspaceId = item.Session?.WorkspaceId;
            sessionIds = item.Session is { } s ? new[] { s.Snapshot.Id } : Array.Empty<Guid>();
        }
        else
        {
            workspaceId = item.Workspace?.Snapshot.Id;
            sessionIds = item.Workspace?.Snapshot.Sessions.Select(s => s.Id).ToArray() ?? Array.Empty<Guid>();
        }

        return new ControlRecentItem
        {
            Id = Wire(item.Id),
            Kind = item.Kind == RecentClosedKind.Session ? "session" : "workspace",
            Title = item.Title,
            Subtitle = item.Subtitle,
            ClosedAt = item.ClosedAt.ToUnixTimeMilliseconds() / 1000.0,
            WorkspaceId = workspaceId is { } w ? Wire(w) : "",
            SessionIds = sessionIds.Select(Wire).ToArray(),
        };
    }

    private static string Wire(Guid id) => id.ToString("D").ToUpperInvariant();
}

/// <summary>What recent.open did. Window is where the entry was restored, which can differ from the
/// requested window when another window already holds its session or workspace. Complete is false
/// when the entry is still in the list afterwards, so a later open can finish the restore.</summary>
public sealed record ControlRecentOpen
{
    [JsonPropertyName("item")]
    public required ControlRecentItem Item { get; init; }

    [JsonPropertyName("window")]
    public required string Window { get; init; }

    [JsonPropertyName("complete")]
    public required bool Complete { get; init; }
}

/// <summary>The outcome of resolving a recent.open target: an entry id, or the wire error text.</summary>
public readonly record struct RecentClosedTargetResult(Guid Id, string? Error);

/// <summary>Host-free resolution of a recent.open target against the current entries.</summary>
public static class RecentClosedTarget
{
    public const string EmptyError = "no recently closed items";

    /// <summary>The newest entry when target is null, else the one whose id matches exactly or by unique
    /// prefix. "active" names nothing here, since the list has no current entry.</summary>
    public static RecentClosedTargetResult Resolve(string? target, IReadOnlyList<RecentClosedItem> items)
    {
        if (items.Count == 0) return new(default, EmptyError);
        if (target is null) return new(items[0].Id, null);

        var resolution = ControlResolve.Resolve(target, items.Select(i => i.Id).ToArray(), null);
        return resolution.Kind == TargetResolution.Outcome.Resolved
            ? new(resolution.Id, null)
            : new(default, ControlResolve.ErrorMessage("recent item", target, resolution));
    }
}
