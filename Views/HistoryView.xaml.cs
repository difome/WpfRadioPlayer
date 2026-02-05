using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RadioPlayer.Models;

namespace RadioPlayer.Views;

public partial class HistoryView : UserControl
{
    public static readonly DependencyProperty PlayHistoryProperty =
        DependencyProperty.Register(
            nameof(PlayHistory), 
            typeof(List<PlayHistoryItem>), 
            typeof(HistoryView), 
            new PropertyMetadata(new List<PlayHistoryItem>(), OnPlayHistoryChanged));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText), 
            typeof(string), 
            typeof(HistoryView), 
            new PropertyMetadata(string.Empty, OnSearchTextChanged));

    private static void OnPlayHistoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HistoryView view)
        {
            view.UpdateHistory();
        }
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HistoryView view)
        {
            view._searchText = e.NewValue?.ToString() ?? string.Empty;
            view.ApplyFilters();
        }
    }

    public List<PlayHistoryItem> PlayHistory
    {
        get => (List<PlayHistoryItem>)GetValue(PlayHistoryProperty);
        set => SetValue(PlayHistoryProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    private System.Collections.ObjectModel.ObservableCollection<PlayHistoryItem> _displayHistory = new();
    private string _currentFilter = "All";
    private string _searchText = string.Empty;

    public HistoryView()
    {
        InitializeComponent();
        HistoryItemsControl.ItemsSource = _displayHistory;
        SetActiveFilter(FilterAllButton);
    }

    private void UpdateHistory()
    {
        ApplyFilters();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            _currentFilter = button.Tag?.ToString() ?? "All";
            SetActiveFilter(button);
            ApplyFilters();
        }
    }

    private void SetActiveFilter(Button activeButton)
    {
        var buttons = new[] { FilterTodayButton, FilterYesterdayButton, FilterAllButton };
        foreach (var btn in buttons)
        {
            if (btn == activeButton)
            {
                btn.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(255, 70, 130, 200));
                btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            }
            else
            {
                btn.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(255, 45, 45, 45));
                btn.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(255, 177, 177, 177));
            }
        }
    }

    private void ApplyFilters()
    {
        _displayHistory.Clear();
        if (PlayHistory == null) return;

        var now = DateTime.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);

        IEnumerable<PlayHistoryItem> filtered = _currentFilter switch
        {
            "Today" => PlayHistory.Where(x => x.PlayedAt.Date == today),
            "Yesterday" => PlayHistory.Where(x => x.PlayedAt.Date == yesterday),
            _ => PlayHistory
        };

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var searchLower = _searchText.ToLowerInvariant();
            filtered = filtered.Where(x =>
                (x.StationTitle ?? string.Empty).ToLowerInvariant().Contains(searchLower) ||
                (x.TrackTitle ?? string.Empty).ToLowerInvariant().Contains(searchLower) ||
                (x.Artist ?? string.Empty).ToLowerInvariant().Contains(searchLower));
        }

        foreach (var item in filtered.OrderByDescending(x => x.PlayedAt))
        {
            _displayHistory.Add(item);
        }
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchTextBox.IsReadOnly)
        {
            SearchTextBox.IsReadOnly = false;
        }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            SearchTextBox.IsReadOnly = true;
        }
        UpdateClearButtonVisibility();
    }

    private void SearchTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SearchTextBox.IsReadOnly)
        {
            SearchTextBox.IsReadOnly = false;
            SearchTextBox.Focus();
            e.Handled = true;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _searchText = textBox.Text ?? string.Empty;
            SearchText = _searchText;
            UpdateClearButtonVisibility();
            ApplyFilters();
        }
    }

    private void UpdateClearButtonVisibility()
    {
        if (ClearSearchButton != null)
        {
            ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text) 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        SearchTextBox.IsReadOnly = true;
        SearchTextBox.Focus();
        UpdateClearButtonVisibility();
    }

    private async void HistoryItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PlayHistoryItem item)
        {
            var trackInfo = $"{item.TrackTitle} - {item.Artist}";
            if (!string.IsNullOrWhiteSpace(trackInfo))
            {
                try
                {
                    Clipboard.SetText(trackInfo);
                    var position = e.GetPosition(this);
                    await ToastNotificationControl.ShowAsync("Скопировано", position, this);
                }
                catch
                {
                }
            }
        }
    }
}
