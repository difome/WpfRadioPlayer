using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RadioPlayer.Models;
using RadioPlayer.Services;

namespace RadioPlayer.Views;

public partial class FavoritesView : UserControl
{
    public static readonly DependencyProperty FavoriteStationsProperty =
        DependencyProperty.Register(nameof(FavoriteStations), typeof(System.Collections.ObjectModel.ObservableCollection<Station>),
            typeof(FavoritesView), new PropertyMetadata(null));

    public System.Collections.ObjectModel.ObservableCollection<Station> FavoriteStations
    {
        get => (System.Collections.ObjectModel.ObservableCollection<Station>)GetValue(FavoriteStationsProperty);
        set => SetValue(FavoriteStationsProperty, value);
    }

    public static readonly DependencyProperty OnStationSelectedProperty =
        DependencyProperty.Register(nameof(OnStationSelected), typeof(ICommand),
            typeof(FavoritesView), new PropertyMetadata(null));

    public ICommand OnStationSelected
    {
        get => (ICommand)GetValue(OnStationSelectedProperty);
        set => SetValue(OnStationSelectedProperty, value);
    }

    public static readonly DependencyProperty OnRemoveFavoriteProperty =
        DependencyProperty.Register(nameof(OnRemoveFavorite), typeof(ICommand),
            typeof(FavoritesView), new PropertyMetadata(null));

    public ICommand OnRemoveFavorite
    {
        get => (ICommand)GetValue(OnRemoveFavoriteProperty);
        set => SetValue(OnRemoveFavoriteProperty, value);
    }

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string),
            typeof(FavoritesView), new PropertyMetadata(string.Empty, OnSearchTextChanged));

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FavoritesView view)
        {
            view.UpdateClearButtonVisibility();
            var binding = view.GetBindingExpression(SearchTextProperty);
            binding?.UpdateSource();

            if (view.DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.SearchText = e.NewValue?.ToString() ?? string.Empty;
            }
        }
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public FavoritesView()
    {
        InitializeComponent();
        Loaded += FavoritesView_Loaded;
    }

    private void FavoritesView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateClearButtonVisibility();
    }

    private void StackPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            if (e.ClickCount == 2)
            {
                OnStationSelected?.Execute(station);
            }
        }
    }

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            OnRemoveFavorite?.Execute(station);
            e.Handled = true;
        }
    }

    private Station? _dragSource;
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging = false;
    private bool _hasMoved = false;

    private void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            _dragSource = station;
            _dragStartPoint = e.GetPosition(null);
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
            _hasMoved = false;

            e.Handled = false;
        }
    }

    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            if (!_hasMoved && !_isDragging && e.ClickCount == 2)
            {
                OnStationSelected?.Execute(station);
            }

            _hasMoved = false;
            _isDragging = false;
        }
    }

    private void Border_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _dragSource != null)
        {
            var border = sender as Border;
            if (border != null && border.DataContext is Station station)
            {
                var currentPoint = e.GetPosition(null);
                var deltaX = Math.Abs(currentPoint.X - _dragStartPoint.X);
                var deltaY = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

                if (deltaX > 5 || deltaY > 5)
                {
                    _hasMoved = true;
                    _isDragging = true;
                    DragDrop.DoDragDrop(border, station, DragDropEffects.Move);
                    _isDragging = false;
                    _dragSource = null;
                    _hasMoved = false;
                }
            }
        }
    }

    private void StackPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Station)))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void StackPanel_Drop(object sender, DragEventArgs e)
    {
        e.Handled = false;
    }

    private void Border_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Station)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            if (sender is Border border)
            {
                var position = e.GetPosition(border);
                var grid = border.Child as Grid;
                if (grid != null)
                {
                    var dropIndicatorTop = grid.FindName("DropIndicatorTop") as Border;
                    var dropIndicatorBottom = grid.FindName("DropIndicatorBottom") as Border;

                    if (position.Y < border.ActualHeight / 2)
                    {
                        if (dropIndicatorTop != null)
                            dropIndicatorTop.Visibility = Visibility.Visible;
                        if (dropIndicatorBottom != null)
                            dropIndicatorBottom.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        if (dropIndicatorTop != null)
                            dropIndicatorTop.Visibility = Visibility.Collapsed;
                        if (dropIndicatorBottom != null)
                            dropIndicatorBottom.Visibility = Visibility.Visible;
                    }
                }
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Border_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            var grid = border.Child as Grid;
            if (grid != null)
            {
                var dropIndicatorTop = grid.FindName("DropIndicatorTop") as Border;
                var dropIndicatorBottom = grid.FindName("DropIndicatorBottom") as Border;
                if (dropIndicatorTop != null)
                    dropIndicatorTop.Visibility = Visibility.Collapsed;
                if (dropIndicatorBottom != null)
                    dropIndicatorBottom.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Border_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            var grid = border.Child as Grid;
            if (grid != null)
            {
                var dropIndicatorTop = grid.FindName("DropIndicatorTop") as Border;
                var dropIndicatorBottom = grid.FindName("DropIndicatorBottom") as Border;
                if (dropIndicatorTop != null)
                    dropIndicatorTop.Visibility = Visibility.Collapsed;
                if (dropIndicatorBottom != null)
                    dropIndicatorBottom.Visibility = Visibility.Collapsed;
            }
        }

        if (e.Data.GetDataPresent(typeof(Station)))
        {
            var droppedStation = (Station)e.Data.GetData(typeof(Station));
            var targetBorder = sender as Border;
            if (targetBorder != null && targetBorder.DataContext is Station targetStation && FavoriteStations != null)
            {
                var sourceIndex = FavoriteStations.IndexOf(droppedStation);
                var targetIndex = FavoriteStations.IndexOf(targetStation);

                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    var position = e.GetPosition(targetBorder);
                    var insertIndex = position.Y < targetBorder.ActualHeight / 2 ? targetIndex : targetIndex + 1;

                    FavoriteStations.RemoveAt(sourceIndex);

                    if (sourceIndex < insertIndex)
                    {
                        insertIndex--;
                    }

                    if (insertIndex < 0)
                        insertIndex = 0;
                    if (insertIndex > FavoriteStations.Count)
                        insertIndex = FavoriteStations.Count;

                    FavoriteStations.Insert(insertIndex, droppedStation);

                    for (int i = 0; i < FavoriteStations.Count; i++)
                    {
                        FavoriteStations[i].Index = i + 1;
                    }
                }
            }
        }
        e.Handled = true;
    }

    private void Border_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromArgb(255, 42, 42, 42));
        }
    }

    private void Border_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
        }
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

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchTextBox.IsReadOnly)
        {
            SearchTextBox.IsReadOnly = false;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var newText = textBox.Text ?? string.Empty;
            SearchText = newText;

            var dataContext = DataContext;
            if (dataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.SearchText = newText;
            }
            else
            {
                var binding = GetBindingExpression(SearchTextProperty);
                binding?.UpdateSource();
            }
        }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchTextBox.IsReadOnly = true;
        }
        UpdateClearButtonVisibility();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchText = string.Empty;
        SearchTextBox.IsReadOnly = true;
        SearchTextBox.Focus();
        UpdateClearButtonVisibility();
    }

    private void UpdateClearButtonVisibility()
    {
        if (ClearSearchButton != null)
        {
            ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(SearchText)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
