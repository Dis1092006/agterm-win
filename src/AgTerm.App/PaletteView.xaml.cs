using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AgTerm.App;

public sealed partial class PaletteView : UserControl
{
    private readonly PaletteController _controller = new();
    private readonly ObservableCollection<PaletteItemViewModel> _items = new();
    private bool _syncing;

    public PaletteView()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _items;
        _controller.Changed += ApplyControllerState;
    }

    public event Action<PaletteItemViewModel>? ItemAccepted;

    public event Action? Cancelled;

    public bool IsOpen => _controller.IsOpen;

    public void Open(string placeholder, IReadOnlyList<PaletteItemViewModel> items)
    {
        Open(placeholder, items, preserveInputOrderForEmptyQuery: false);
    }

    public void Open(
        string placeholder,
        IReadOnlyList<PaletteItemViewModel> items,
        bool preserveInputOrderForEmptyQuery)
    {
        if (string.IsNullOrWhiteSpace(placeholder))
            throw new ArgumentException("Palette placeholder is required.", nameof(placeholder));
        Open(placeholder, items, new PaletteOpenOptions { PreserveInputOrderForEmptyQuery = preserveInputOrderForEmptyQuery });
    }

    /// <summary>The pick path: an empty placeholder is allowed because a caller's prompt may be "".</summary>
    public void Open(string placeholder, IReadOnlyList<PaletteItemViewModel> items, PaletteOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        QueryBox.PlaceholderText = placeholder;
        Visibility = Visibility.Visible;
        ApplyAvailableHeight(ActualHeight);
        _controller.Open(items, options);
        FocusQueryBox();
        DispatcherQueue.TryEnqueue(FocusQueryBox);
        if (options.InitialQuery.Length > 0)
        {
            // a seeded query opens selected so the first keystroke replaces it, as upstream
            DispatcherQueue.TryEnqueue(() => QueryBox.SelectAll());
        }
    }

    public Func<VirtualKey, bool>? CancelChord { get; set; }

    /// <summary>Tells whether the host window is active. The click that activates a window is also
    /// delivered to the scrim, and it must not cancel a pick shown in the background.</summary>
    public Func<bool>? IsWindowActive { get; set; }

    public void Close()
    {
        if (!_controller.IsOpen)
            return;

        _controller.Close();
        Visibility = Visibility.Collapsed;
    }

    private void Cancel()
    {
        if (!_controller.IsOpen)
            return;

        Close();
        Cancelled?.Invoke();
    }

    private void AcceptSelected()
    {
        var selected = _controller.AcceptSelected();
        if (selected is null)
            return;

        Visibility = Visibility.Collapsed;
        ItemAccepted?.Invoke(selected);
    }

    private void ApplyControllerState()
    {
        _syncing = true;
        try
        {
            if (QueryBox.Text != _controller.Query)
                QueryBox.Text = _controller.Query;

            _items.Clear();
            foreach (var item in _controller.FilteredItems)
                _items.Add(item);

            ResultsList.SelectedIndex = _controller.SelectedItem is null
                ? -1
                : _controller.SelectedIndex;
            if (_controller.SelectedItem is { } selected)
                ResultsList.ScrollIntoView(selected);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void FocusQueryBox() => QueryBox.Focus(FocusState.Programmatic);

    private void ApplyAvailableHeight(double availableHeight)
    {
        PalettePanel.Margin = new Thickness(
            24,
            PaletteLayout.PanelTopMargin(availableHeight),
            24,
            24);
        ResultsList.Height = PaletteLayout.ResultHeight(availableHeight);
    }

    private void OnPaletteSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyAvailableHeight(e.NewSize.Height);

    private void OnQueryBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
            _controller.SetQuery(QueryBox.Text);
    }

    private void OnQueryBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = PaletteKeys.SelectionStep((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Shift), IsDown(VirtualKey.Menu));
        if (step != 0)
        {
            _controller.MoveSelection(step);
            e.Handled = true;
            return;
        }
        if (CancelChord?.Invoke(e.Key) == true)
        {
            Cancel();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down:
                _controller.MoveSelection(1);
                break;
            case VirtualKey.Up:
                _controller.MoveSelection(-1);
                break;
            case VirtualKey.Enter:
                AcceptSelected();
                break;
            case VirtualKey.Escape:
                Cancel();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ResultsList.SelectedIndex >= 0)
            _controller.SelectIndex(ResultsList.SelectedIndex);
    }

    private void OnResultItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PaletteItemViewModel item)
            return;

        var index = _items.IndexOf(item);
        if (index >= 0)
            _controller.SelectIndex(index);
        AcceptSelected();
    }

    private bool _pressStartedInactive;

    private void OnDismissSurfacePointerPressed(object sender, PointerRoutedEventArgs e) =>
        _pressStartedInactive = IsWindowActive?.Invoke() == false;

    private void OnDismissSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        var ignore = _pressStartedInactive;
        _pressStartedInactive = false;
        if (ignore)
            return;
        Cancel();
    }
}