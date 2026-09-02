using System.Collections.ObjectModel;
using AgTerm.Core.Model;

namespace AgTerm.App;

public sealed record PaletteItemViewModel
{
    public PaletteItemViewModel(
        string id,
        string title,
        string? subtitle = null,
        string? shortcut = null,
        string? badge = null,
        AgentStatus? status = null,
        string? statusColor = null,
        StatusShape? statusShape = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Palette item id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Palette item title is required.", nameof(title));

        Id = id;
        Title = title;
        Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle;
        Shortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut;
        Badge = string.IsNullOrWhiteSpace(badge) ? null : badge;
        Status = status;
        StatusColor = string.IsNullOrWhiteSpace(statusColor) ? null : statusColor;
        StatusShape = statusShape;

        var searchKeys = Subtitle is null ? new[] { Title } : new[] { Title, Subtitle };
        SearchKeys = new ReadOnlyCollection<string>(searchKeys);
        AutomationName = string.Join(", ", new[]
            {
                Title,
                Subtitle,
                Status is null ? null : $"{Status.Value.ToString().ToLowerInvariant()} status",
                Badge,
                Shortcut,
            }
            .Where(value => value is not null));
    }

    public string Id { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public string? Shortcut { get; }

    public string? Badge { get; }

    public AgentStatus? Status { get; }

    public string? StatusColor { get; }

    public StatusShape? StatusShape { get; }

    public string StatusGlyph => Status is null or AgentStatus.Idle
        ? string.Empty
        : StatusShape switch
        {
            Core.Model.StatusShape.Square => "■",
            Core.Model.StatusShape.Triangle => "▲",
            Core.Model.StatusShape.Diamond => "◆",
            Core.Model.StatusShape.Capsule => "▬",
            Core.Model.StatusShape.Star => "★",
            _ => "●",
        };

    public IReadOnlyList<string> SearchKeys { get; }

    public string AutomationName { get; }
}