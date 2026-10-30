using System.Collections.ObjectModel;
using AgTerm.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AgTerm.App;

public sealed partial class PaletteView : UserControl
{
    private readonly PaletteController _controller = new();
    private readonly ObservableCollection<PaletteItemViewModel> _items = new();
    private readonly ActivationClickFilter _activationClicks = new();
    private bool _syncing;
    private IReadOnlyList<PaletteItemViewModel>? _shownItems;

    public PaletteView()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _items;
        _controller.Changed += ApplyControllerState;
        _controller.SelectionChanged += item => SelectionChanged?.Invoke(item);
    }

    public event Action<PaletteItemViewModel>? ItemAccepted;

    public event Action? Cancelled;

    public event Action<PaletteItemViewModel?>? SelectionChanged;

    public bool IsOpen => _controller.IsOpen;

    public void RefreshIndicators(IReadOnlyDictionary<Guid, AgentIndicator> indicators, AppSettings settings, bool liveSessions) =>
        _controller.RefreshIndicators(indicators, settings, liveSessions);

    public void SetPanelBackground(Brush? background)
    {
        if (background is null)
            PalettePanel.ClearValue(Border.BackgroundProperty);
        else
            PalettePanel.Background = background;
    }

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
    public void Open(string placeholder, IReadOnlyList<PaletteItemViewModel> items, PaletteOpenOptions options,
                     bool takeFocus = true)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        QueryBox.PlaceholderText = placeholder;
        Visibility = Visibility.Visible;
        ApplyAvailableHeight(ActualHeight);
        _controller.Open(items, options);
        // focusing the query box activates the window, which a pick sent to a background window
        // must not do; the host calls FocusQuery when the user activates the window
        if (takeFocus)
            FocusQuery();
        if (options.InitialQuery.Length > 0)
        {
            // a seeded query opens selected so the first keystroke replaces it, as upstream
            DispatcherQueue.TryEnqueue(() => QueryBox.SelectAll());
        }
    }

    public Func<VirtualKey, bool>? CancelChord { get; set; }

    /// <summary>The host window calls this on every activation. The click that activates a window
    /// also reaches the scrim, and it must not cancel a pick shown in the background.</summary>
    public void NoteActivation() => _activationClicks.Activated(Environment.TickCount64);

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

            // a selection move keeps the controller's list; rebuilding 600 theme rows on every key
            // press made holding an arrow key lag and reset the list's scroll
            if (!ReferenceEquals(_shownItems, _controller.FilteredItems))
            {
                _items.Clear();
                foreach (var item in _controller.FilteredItems)
                    _items.Add(item);
                _shownItems = _controller.FilteredItems;
            }

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

    public void FocusQuery()
    {
        FocusQueryUnlessContentDialogOpen();
        DispatcherQueue.TryEnqueue(FocusQueryUnlessContentDialogOpen);
    }

    private void FocusQueryUnlessContentDialogOpen()
    {
        // programmatic focus leaves the modal dialog, and the query box under its backdrop then takes
        // the keys typed for the dialog; the host focuses the query again when the dialog closes
        if (!IsContentDialogOpen())
            QueryBox.Focus(FocusState.Programmatic);
    }

    private bool IsContentDialogOpen() =>
        XamlRoot is not null
        && VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(popup => popup.Child is ContentDialog);

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
        if (PaletteKeys.LeavesToWindow((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift)))
            return;

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

    private void OnDismissSurfacePointerPressed(object sender, PointerRoutedEventArgs e) =>
        _activationClicks.Pressed(Environment.TickCount64);

    private void OnDismissSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_activationClicks.ShouldIgnoreTap())
            return;
        Cancel();
    }
}