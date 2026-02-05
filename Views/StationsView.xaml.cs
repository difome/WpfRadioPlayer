using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RadioPlayer.Models;

namespace RadioPlayer.Views;

public partial class StationsView : UserControl
{
    public static readonly DependencyProperty StationsProperty =
        DependencyProperty.Register(nameof(Stations), typeof(System.Collections.ObjectModel.ObservableCollection<Station>),
            typeof(StationsView), new PropertyMetadata(null));

    public System.Collections.ObjectModel.ObservableCollection<Station> Stations
    {
        get => (System.Collections.ObjectModel.ObservableCollection<Station>)GetValue(StationsProperty);
        set => SetValue(StationsProperty, value);
    }

    public static readonly DependencyProperty OnStationSelectedProperty =
        DependencyProperty.Register(nameof(OnStationSelected), typeof(ICommand),
            typeof(StationsView), new PropertyMetadata(null));

    public ICommand OnStationSelected
    {
        get => (ICommand)GetValue(OnStationSelectedProperty);
        set => SetValue(OnStationSelectedProperty, value);
    }

    public static readonly DependencyProperty OnToggleFavoriteProperty =
        DependencyProperty.Register(nameof(OnToggleFavorite), typeof(ICommand),
            typeof(StationsView), new PropertyMetadata(null));

    public ICommand OnToggleFavorite
    {
        get => (ICommand)GetValue(OnToggleFavoriteProperty);
        set => SetValue(OnToggleFavoriteProperty, value);
    }

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string),
            typeof(StationsView), new PropertyMetadata(string.Empty, OnSearchTextChanged));

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StationsView view)
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

    public static readonly DependencyProperty OnSaveOrderProperty =
        DependencyProperty.Register(nameof(OnSaveOrder), typeof(ICommand),
            typeof(StationsView), new PropertyMetadata(null));

    public ICommand OnSaveOrder
    {
        get => (ICommand)GetValue(OnSaveOrderProperty);
        set => SetValue(OnSaveOrderProperty, value);
    }

    public StationsView()
    {
        InitializeComponent();
        Loaded += StationsView_Loaded;
    }

    private void StationsView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateClearButtonVisibility();
    }

    private void Border_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 42, 42, 42));
        }
    }

    private void Border_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private Station? _dragSource;

    private void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            _dragSource = station;
            _dragStartPoint = e.GetPosition(null);
        }
    }

    private System.Windows.Point _dragStartPoint;
    private DateTime _lastClickTime = DateTime.MinValue;
    private bool _isDragging = false;
    private bool _hasMoved = false;

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
            if (!_hasMoved && !_isDragging)
            {
                if (e.ClickCount == 2)
                {
                    OnStationSelected?.Execute(station);
                }
                else if (e.ClickCount == 1)
                {
                    try
                    {
                        if (DataContext is ViewModels.MainViewModel viewModel)
                        {
                            viewModel.SelectStationForAction(station);
                        }
                        if (sender is Border border)
                        {
                            border.Focus();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in Border_MouseLeftButtonUp: {ex.Message}");
                    }
                }
            }

            _hasMoved = false;
            _isDragging = false;
        }
    }

    private void Border_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is Station station)
                {
                    if (DataContext is ViewModels.MainViewModel viewModel)
                    {
                        viewModel.DeleteStation(station);
                    }
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Border_KeyDown: {ex.Message}");
            }
        }
        else if (e.Key == Key.Enter)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is Station station)
                {
                    OnStationSelected?.Execute(station);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Border_KeyDown Enter: {ex.Message}");
            }
        }
    }

    private void ScrollViewer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            try
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                {
                    viewModel.DeleteStation();
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ScrollViewer_KeyDown: {ex.Message}");
            }
        }
        else if (e.Key == Key.Enter)
        {
            try
            {
                if (Stations != null)
                {
                    var selectedStation = Stations.FirstOrDefault(s => s.IsSelected);
                    if (selectedStation != null)
                    {
                        OnStationSelected?.Execute(selectedStation);
                        e.Handled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ScrollViewer_KeyDown Enter: {ex.Message}");
            }
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu contextMenu)
        {
            var popup = System.Windows.Media.VisualTreeHelper.GetParent(contextMenu) as System.Windows.Controls.Primitives.Popup;
            if (popup != null)
            {
                popup.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
                popup.AllowsTransparency = false;
            }

            if (contextMenu.PlacementTarget is Border border)
            {
                if (border.DataContext is Station station)
                {
                    if (DataContext is ViewModels.MainViewModel viewModel)
                    {
                        viewModel.SelectStationForAction(station);
                    }
                }
            }
        }
    }

    private void MenuItem_Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is Border border && border.DataContext is Station station)
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                {
                    viewModel.PlayStationCommand?.Execute(station);
                }
            }
        }
    }

    private void MenuItem_Add_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel viewModel)
        {
            viewModel.AddStationCommand?.Execute(null);
        }
    }

    private void MenuItem_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is Border border && border.DataContext is Station station)
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                {
                    viewModel.DeleteStationCommand?.Execute(station);
                }
            }
        }
    }

    private void MenuItem_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is Border border && border.DataContext is Station station)
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                {
                    viewModel.EditStationCommand?.Execute(station);
                }
            }
        }
    }

    private void MenuItem_Metadata_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is Border border && border.DataContext is Station station)
            {
                if (DataContext is ViewModels.MainViewModel viewModel)
                {
                    viewModel.ViewMetadataCommand?.Execute(station);
                }
            }
        }
    }

    private void StackPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            if (e.ClickCount == 2)
            {
                OnStationSelected?.Execute(station);
                e.Handled = true;
            }
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
            if (targetBorder != null && targetBorder.DataContext is Station targetStation && Stations != null)
            {
                var sourceIndex = Stations.IndexOf(droppedStation);
                var targetIndex = Stations.IndexOf(targetStation);

                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    var position = e.GetPosition(targetBorder);
                    var insertIndex = position.Y < targetBorder.ActualHeight / 2 ? targetIndex : targetIndex + 1;

                    Stations.RemoveAt(sourceIndex);

                    if (sourceIndex < insertIndex)
                    {
                        insertIndex--;
                    }

                    if (insertIndex < 0)
                        insertIndex = 0;
                    if (insertIndex > Stations.Count)
                        insertIndex = Stations.Count;

                    Stations.Insert(insertIndex, droppedStation);

                    for (int i = 0; i < Stations.Count; i++)
                    {
                        Stations[i].Index = i + 1;
                    }

                    OnSaveOrder?.Execute(null);
                }
            }
        }
        e.Handled = true;
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Station station)
        {
            OnToggleFavorite?.Execute(station);
            e.Handled = true;
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

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
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

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta * 1.5);
            e.Handled = true;
        }
    }

}
