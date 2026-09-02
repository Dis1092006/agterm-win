using System.Collections.ObjectModel;

namespace AgTerm.App;

public sealed record PaletteItemViewModel
{
    public PaletteItemViewModel(
        string id,
        string title,
        string? subtitle = null,
        string? shortcut = null,
        string? badge = null)
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

        var searchKeys = Subtitle is null ? new[] { Title } : new[] { Title, Subtitle };
        SearchKeys = new ReadOnlyCollection<string>(searchKeys);
        AutomationName = string.Join(", ", new[] { Title, Subtitle, Badge, Shortcut }
            .Where(value => value is not null));
    }

    public string Id { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public string? Shortcut { get; }

    public string? Badge { get; }

    public IReadOnlyList<string> SearchKeys { get; }

    public string AutomationName { get; }
}