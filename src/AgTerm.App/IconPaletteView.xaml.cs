using AgTerm.Core.Icons;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AgTerm.App;

/// <summary>A grid cell as the template shows it. The Default cell has no glyph of its own, so it
/// shows a cross.</summary>
public sealed class IconPaletteCellItem(IconPaletteCell cell)
{
    private const string DefaultGlyph = "";

    public IconPaletteCell Cell { get; } = cell;

    public string Glyph => Cell.Glyph ?? DefaultGlyph;

    public string Label => Cell.Label;
}

public sealed partial class IconPaletteView : UserControl
{
    private const double CellSize = 36;
    private const double ScrollBarAllowance = 24;
    private const double LabelRowHeight = 32;

    private readonly ActivationClickFilter _activationClicks = new();
    private IconPaletteModel _model = new([], _ => true, null);
    private IReadOnlyList<IconPaletteCell>? _shownCells;
    private List<IconPaletteCellItem> _items = [];
    private bool _syncing;
    private bool _open;

    public IconPaletteView()
    {
        InitializeComponent();
    }

    /// <summary>The selection moved: the cell now selected, or null when the filter left none.</summary>
    public event Action<IconPaletteCell?>? SelectionChanged;

    public event Action<string?>? Accepted;

    public event Action? Cancelled;

    public bool IsOpen => _open;

    public Func<VirtualKey, bool>? CancelChord { get; set; }

    public void SetPanelBackground(Brush? background)
    {
        if (background is null)
            PalettePanel.ClearValue(Border.BackgroundProperty);
        else
            PalettePanel.Background = background;
    }

    public void NoteActivation() => _activationClicks.Activated(Environment.TickCount64);

    public void Open(string? currentIcon)
    {
        _model = new IconPaletteModel(SegoeIconCatalog.All, SymbolGlyphs.Has, currentIcon);
        _shownCells = null;
        QueryBox.Text = "";
        _open = true;
        Visibility = Visibility.Visible;
        ApplyAvailableHeight(ActualHeight);
        ShowModel(notify: false);
        FocusFilter();
    }

    public void Close()
    {
        if (!_open)
            return;

        _open = false;
        Visibility = Visibility.Collapsed;
    }

    public void FocusFilter()
    {
        FocusFilterUnlessContentDialogOpen();
        DispatcherQueue.TryEnqueue(FocusFilterUnlessContentDialogOpen);
    }

    private void FocusFilterUnlessContentDialogOpen()
    {
        if (!IsContentDialogOpen())
            QueryBox.Focus(FocusState.Programmatic);
    }

    private bool IsContentDialogOpen() =>
        XamlRoot is not null
        && VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(popup => popup.Child is ContentDialog);

    private void Cancel()
    {
        if (!_open)
            return;

        Close();
        Cancelled?.Invoke();
    }

    private void Accept()
    {
        if (!_open || !_model.CanAccept)
            return;

        var name = _model.SelectedName;
        Close();
        Accepted?.Invoke(name);
    }

    private void Move(IconPaletteMove move)
    {
        _model.Move(move, Columns(), RowsPerPage());
        ShowModel(notify: true);
    }

    private int Columns() =>
        Math.Max(1, (int)((IconGrid.ActualWidth - IconGrid.Padding.Left - IconGrid.Padding.Right - ScrollBarAllowance) / CellSize));

    private int RowsPerPage() =>
        Math.Max(1, (int)((IconGrid.ActualHeight - IconGrid.Padding.Top - IconGrid.Padding.Bottom) / CellSize));

    private void ShowModel(bool notify)
    {
        _syncing = true;
        try
        {
            // a selection move keeps the cells; rebuilding 1500 of them on every key press made
            // holding an arrow key lag
            if (!ReferenceEquals(_shownCells, _model.Cells))
            {
                _items = _model.Cells.Select(cell => new IconPaletteCellItem(cell)).ToList();
                IconGrid.ItemsSource = _items;
                _shownCells = _model.Cells;
            }

            if (_model.Selected is { } index)
            {
                IconGrid.SelectedIndex = index;
                IconGrid.ScrollIntoView(_items[index]);
                LabelText.Text = _model.Cells[index].Label;
            }
            else
            {
                IconGrid.SelectedIndex = -1;
                LabelText.Text = "";
            }
        }
        finally
        {
            _syncing = false;
        }

        if (notify)
            SelectionChanged?.Invoke(_model.Selected is { } selected ? _model.Cells[selected] : null);
    }

    private void ApplyAvailableHeight(double availableHeight)
    {
        PalettePanel.Margin = new Thickness(24, PaletteLayout.PanelTopMargin(availableHeight), 24, 24);
        IconGrid.Height = Math.Max(CellSize, PaletteLayout.ResultHeight(availableHeight) - LabelRowHeight);
    }

    private void OnPaletteSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyAvailableHeight(e.NewSize.Height);

    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_open && _model.Selected is { } index && index < _items.Count)
            IconGrid.ScrollIntoView(_items[index]);
    }

    private void OnQueryBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_open)
            return;

        _model.Filter(QueryBox.Text);
        ShowModel(notify: true);
    }

    private void OnQueryBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = PaletteKeys.SelectionStep((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Shift), IsDown(VirtualKey.Menu));
        if (step != 0)
        {
            Move(step > 0 ? IconPaletteMove.Down : IconPaletteMove.Up);
            e.Handled = true;
            return;
        }
        if (CancelChord?.Invoke(e.Key) == true)
        {
            Cancel();
            e.Handled = true;
            return;
        }
        if (PaletteKeys.LeavesToWindow((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift)))
            return;

        switch (e.Key)
        {
            case VirtualKey.Left: Move(IconPaletteMove.Left); break;
            case VirtualKey.Right: Move(IconPaletteMove.Right); break;
            case VirtualKey.Up: Move(IconPaletteMove.Up); break;
            case VirtualKey.Down: Move(IconPaletteMove.Down); break;
            case VirtualKey.PageUp: Move(IconPaletteMove.PageUp); break;
            case VirtualKey.PageDown: Move(IconPaletteMove.PageDown); break;
            case VirtualKey.Home: Move(IconPaletteMove.Home); break;
            case VirtualKey.End: Move(IconPaletteMove.End); break;
            case VirtualKey.Enter: Accept(); break;
            case VirtualKey.Escape: Cancel(); break;
            default: return;
        }

        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || IconGrid.SelectedIndex < 0)
            return;

        _model.Select(IconGrid.SelectedIndex);
        ShowModel(notify: true);
        FocusFilter();
    }

    private void OnGridDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: IconPaletteCellItem })
            Accept();
    }

    private void OnDismissSurfacePointerPressed(object sender, PointerRoutedEventArgs e) =>
        _activationClicks.Pressed(Environment.TickCount64);

    private void OnDismissSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_activationClicks.ShouldIgnoreTap())
            return;
        Cancel();
    }
}
