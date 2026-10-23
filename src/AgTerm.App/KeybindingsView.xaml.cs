using AgTerm.Core.Keymap;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AgTerm.App;

public sealed partial class KeybindingsView : UserControl
{
    private readonly KeybindingsController _controller = new();
    public KeybindingsView() => InitializeComponent();
    public bool IsOpen => _controller.IsOpen;
    public event Action? CloseRequested;

    public void Open(IReadOnlyList<KeybindingGroup> groups)
    {
        _controller.Open(groups);
        QueryBox.Text = "";
        Visibility = Visibility.Visible;
        ShowGroups();
        FocusQuery();
    }

    public void Refresh(IReadOnlyList<KeybindingGroup> groups)
    {
        _controller.Refresh(groups);
        ShowGroups();
    }

    public void Close()
    {
        _controller.Close();
        Visibility = Visibility.Collapsed;
    }

    public void FocusQuery()
    {
        FocusSearch();
        DispatcherQueue.TryEnqueue(FocusSearch);
    }

    private void FocusSearch()
    {
        if (IsOpen)
            QueryBox.Focus(FocusState.Programmatic);
    }

    private void ShowGroups()
    {
        GroupedRows.Source = _controller.Groups;
        EmptyMessage.Visibility = _controller.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        _controller.SetQuery(QueryBox.Text);
        ShowGroups();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;
        e.Handled = true;
        CloseRequested?.Invoke();
    }

    private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        if (!IsOpen || args.NewFocusedElement is not { } target)
            return;
        for (var node = target; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, this))
                return;
        if (!args.TrySetNewFocusedElement(QueryBox))
            args.TryCancel();
    }
}
