using AgTerm.Core.Icons;

namespace AgTerm.Xterm;

public enum IconPaletteMove { Left, Right, Up, Down, PageUp, PageDown, Home, End }

/// <summary>One grid cell. Name is null for the Default cell, which has no glyph.</summary>
public sealed record IconPaletteCell(string? Name, string Label, string? Glyph);

/// <summary>
/// The cells, the filter and the selection behind the icon palette grid. The view owns the layout and
/// passes the column count and page height with each move.
/// </summary>
public sealed class IconPaletteModel
{
    private static readonly IconPaletteCell DefaultCell = new(null, "Default", null);

    private readonly IReadOnlyList<IconPaletteCell> _all;
    private string? _rememberedName;

    public IconPaletteModel(IReadOnlyList<SegoeIcon> icons, Func<int, bool> hasGlyph, string? current)
    {
        var cells = new List<IconPaletteCell> { DefaultCell };
        foreach (var icon in icons)
        {
            if (!hasGlyph(icon.CodePoint)) continue;
            cells.Add(new IconPaletteCell(icon.Name, icon.Name, char.ConvertFromUtf32(icon.CodePoint)));
        }
        _all = cells;
        Cells = cells;
        _rememberedName = current;
        Selected = IndexOfName(current) ?? 0;
    }

    public IReadOnlyList<IconPaletteCell> Cells { get; private set; }

    public int? Selected { get; private set; }

    public string? SelectedName => Selected is { } i ? Cells[i].Name : null;

    public bool CanAccept => Selected is not null;

    public void Filter(string text)
    {
        // An empty result clears the selection, so the last selected name is kept to restore it.
        if (Selected is { } before) _rememberedName = Cells[before].Name;

        var needle = (text ?? "").Trim();
        Cells = needle.Length == 0
            ? _all
            : _all.Where(c => c.Name is not null && c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

        Selected = Cells.Count == 0 ? null : IndexOfName(_rememberedName) ?? 0;
    }

    public void Move(IconPaletteMove move, int columns, int rowsPerPage)
    {
        if (Selected is not { } from) return;
        columns = Math.Max(columns, 1);
        rowsPerPage = Math.Max(rowsPerPage, 1);
        var last = Cells.Count - 1;
        var target = move switch
        {
            IconPaletteMove.Left => from - 1,
            IconPaletteMove.Right => from + 1,
            IconPaletteMove.Up => from < columns ? from : from - columns,
            IconPaletteMove.Down => from + columns,
            IconPaletteMove.PageUp => from - columns * rowsPerPage,
            IconPaletteMove.PageDown => from + columns * rowsPerPage,
            IconPaletteMove.Home => 0,
            _ => last,
        };
        Selected = Math.Clamp(target, 0, last);
    }

    public void Select(int index)
    {
        if (index >= 0 && index < Cells.Count) Selected = index;
    }

    private int? IndexOfName(string? name)
    {
        for (var i = 0; i < Cells.Count; i++)
            if (string.Equals(Cells[i].Name, name, StringComparison.Ordinal)) return i;
        return null;
    }
}
