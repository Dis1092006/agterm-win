namespace AgTerm.Core.Keymap;

public sealed class KeybindingsController
{
    private IReadOnlyList<KeybindingGroup> _allGroups = [];
    public bool IsOpen { get; private set; }
    public string Query { get; private set; } = "";
    public IReadOnlyList<KeybindingGroup> Groups { get; private set; } = [];

    public void Open(IReadOnlyList<KeybindingGroup> groups)
    {
        IsOpen = true;
        Query = "";
        Refresh(groups);
    }

    public void Refresh(IReadOnlyList<KeybindingGroup> groups)
    {
        _allGroups = groups;
        Groups = KeybindingsCatalog.Filter(_allGroups, Query);
    }

    public void SetQuery(string query)
    {
        Query = query;
        Groups = KeybindingsCatalog.Filter(_allGroups, query);
    }

    public void Close() => IsOpen = false;
}
