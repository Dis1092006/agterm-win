using System.Collections.ObjectModel;
using AgTerm.Core.Model;

namespace AgTerm.App;

public sealed class PaletteController
{
    private IReadOnlyList<PaletteItemViewModel> _allItems = Array.Empty<PaletteItemViewModel>();
    private IReadOnlyList<PaletteItemViewModel> _filteredItems = Array.Empty<PaletteItemViewModel>();
    private PaletteOpenOptions _options = new();

    public event Action? Changed;

    public bool IsOpen { get; private set; }

    public string Query { get; private set; } = string.Empty;

    public int SelectedIndex { get; private set; }

    public IReadOnlyList<PaletteItemViewModel> FilteredItems => _filteredItems;

    public PaletteItemViewModel? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < _filteredItems.Count
            ? _filteredItems[SelectedIndex]
            : null;

    public void Open(
        IEnumerable<PaletteItemViewModel> items,
        bool preserveInputOrderForEmptyQuery = false) =>
        Open(items, new PaletteOpenOptions { PreserveInputOrderForEmptyQuery = preserveInputOrderForEmptyQuery });

    public void Open(IEnumerable<PaletteItemViewModel> items, PaletteOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = items.ToArray();
        if (snapshot.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Palette item ids must be unique.", nameof(items));

        _allItems = new ReadOnlyCollection<PaletteItemViewModel>(snapshot);
        _options = options;
        Query = options.InitialQuery;
        IsOpen = true;
        Refilter();
        SelectedIndex = InitialSelectionIndex(options.InitialSelectionId);
        Changed?.Invoke();
    }

    public void SetQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsOpen || Query == query)
            return;

        Query = query;
        SelectedIndex = 0;
        Refilter();
        Changed?.Invoke();
    }

    public void MoveSelection(int delta)
    {
        if (!IsOpen || _filteredItems.Count == 0 || delta == 0)
            return;

        var next = Math.Clamp((long)SelectedIndex + delta, 0, _filteredItems.Count - 1);
        if (next == SelectedIndex)
            return;

        SelectedIndex = (int)next;
        Changed?.Invoke();
    }

    public void SelectIndex(int index)
    {
        if (index < 0 || index >= _filteredItems.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (SelectedIndex == index)
            return;

        SelectedIndex = index;
        Changed?.Invoke();
    }

    public PaletteItemViewModel? AcceptSelected()
    {
        var selected = SelectedItem;
        if (selected is not null)
        {
            Close();
        }

        return selected;
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        Query = string.Empty;
        SelectedIndex = 0;
        _allItems = Array.Empty<PaletteItemViewModel>();
        _filteredItems = Array.Empty<PaletteItemViewModel>();
        _options = new PaletteOpenOptions();
        Changed?.Invoke();
    }

    /// <summary>An initial selection that the query hides falls back to the first row, so a pick
    /// never opens with nothing highlighted.</summary>
    private int InitialSelectionIndex(string? selectionId)
    {
        if (selectionId is null)
            return 0;

        for (var index = 0; index < _filteredItems.Count; index++)
        {
            var row = _filteredItems[index];
            if (!row.IsCustomAnswer && row.Id == selectionId)
                return index;
        }

        return 0;
    }

    private void Refilter()
    {
        var blank = string.IsNullOrWhiteSpace(Query);
        Func<PaletteItemViewModel, IReadOnlyList<string>> keys = _options.MatchTitleOnly
            ? static item => item.TitleKeys
            : static item => item.SearchKeys;

        IReadOnlyList<PaletteItemViewModel> rows = blank && _options.PreserveInputOrderForEmptyQuery
            ? _allItems
            : FuzzyMatcher.Rank(Query.Trim(), _allItems, keys);

        if (_options.AllowCustomAnswer && !blank && rows.Count == 0)
            rows = new[] { PaletteItemViewModel.ForCustomAnswer(Query.Trim()) };

        _filteredItems = new ReadOnlyCollection<PaletteItemViewModel>(rows.ToArray());
    }
}