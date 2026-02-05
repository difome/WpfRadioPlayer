using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using RadioPlayer.ViewModels;
using RadioPlayer.Views;
using RadioPlayer.Services;
using RadioPlayer.Models;
using System.Windows.Shapes;

namespace RadioPlayer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = null!;
    private readonly StationService _stationService = new();
    private NotifyIcon? _notifyIcon;
    private bool _isClosing = false;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_STYLE = -16;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (XamlParseException xpe)
        {
            Trace.TraceError(xpe.ToString());
            System.Windows.MessageBox.Show("Failed to load window XAML:\n" + xpe.Message, "XAML load error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current?.Shutdown();
            return;
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
            System.Windows.MessageBox.Show("Unexpected error while initializing window:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current?.Shutdown();
            return;
        }

        _viewModel = new MainViewModel();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.RequestAddStation += ViewModel_RequestAddStation;
        _viewModel.RequestEditStation += ViewModel_RequestEditStation;
        _viewModel.RequestViewMetadata += ViewModel_RequestViewMetadata;
        DataContext = _viewModel;
        UpdateView();
        Closing += MainWindow_Closing;
        SizeChanged += MainWindow_SizeChanged;

        Services.Logger.SetConsoleVisible(_viewModel.ShowConsole);

        InitializeTrayIcon();
        LoadWindowSize();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(int)(WS_CAPTION | WS_SYSMENU);
            SetWindowLong(hwnd, GWL_STYLE, (uint)style);
            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED);
        }

        UpdateTabStyles();
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (WindowState == WindowState.Normal && e.PreviousSize.Width > 0 && e.PreviousSize.Height > 0)
        {
            SaveWindowSize();
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(int)(WS_CAPTION | WS_SYSMENU);
            SetWindowLong(hwnd, GWL_STYLE, (uint)style);
            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED);
        }
    }

    private void LoadWindowSize()
    {
        var settings = _stationService.LoadSettings();
        if (settings.WindowWidth >= MinWidth)
        {
            Width = settings.WindowWidth;
        }
        if (settings.WindowHeight >= MinHeight)
        {
            Height = settings.WindowHeight;
        }
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
    }

    private void SaveWindowSize()
    {
        var settings = _stationService.LoadSettings();
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        _stationService.SaveSettings(settings);
    }

    private void InitializeTrayIcon()
    {
        Icon? icon = null;
        try
        {
            var iconUri = new Uri("pack://application:,,,/radio_player.ico");
            var info = System.Windows.Application.GetResourceStream(iconUri);
            if (info != null)
            {
                using (var stream = info.Stream)
                {
                    icon = new Icon(stream);
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "radio_player.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    icon = new Icon(iconPath);
                }
            }
            catch { }

            if (icon == null)
            {
                Services.Logger.LogError("Не удалось загрузить иконку для трея из ресурсов или файла", ex);
            }
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "Радио плеер",
            Visible = true
        };

        _notifyIcon.DoubleClick += (sender, e) =>
        {
            ShowWindow();
        };

        var contextMenu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("Показать", null, (s, e) => ShowWindow());
        var exitItem = new ToolStripMenuItem("Выход", null, (s, e) => ExitApplication());
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void MinimizeToTray()
    {
        Hide();
    }

    private void ExitApplication()
    {
        _isClosing = true;
        _notifyIcon?.Dispose();
        Close();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var source = e.OriginalSource;
        if (source is System.Windows.Controls.Button ||
            source is System.Windows.Controls.Slider ||
            source is System.Windows.Controls.Primitives.Thumb ||
            source is System.Windows.Controls.TextBlock tb && tb.Cursor == System.Windows.Input.Cursors.Hand)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _isClosing = true;
        Close();
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        MinimizeToTray();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        var aboutDialog = new Views.AboutDialog(_viewModel)
        {
            Owner = this
        };
        aboutDialog.ShowDialog();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        SaveWindowSize();
        _viewModel.SaveSettings();
        _viewModel.SaveStationsOrder();
        _viewModel.Dispose();
        _notifyIcon?.Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            UpdateView();
            UpdateTabStyles();
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowConsole))
        {
            Services.Logger.SetConsoleVisible(_viewModel.ShowConsole);
        }
    }

    private void StationsTabPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 0)
        {
            TabStationsUnderline.Visibility = Visibility.Visible;
            TabStations.Foreground = new SolidColorBrush(Colors.White);
        }
    }

    private void StationsTabPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 0)
        {
            TabStationsUnderline.Visibility = Visibility.Collapsed;
            TabStations.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        }
    }

    private void TabStations_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 0)
        {
            TabStationsUnderline.Visibility = Visibility.Visible;
            TabStations.Foreground = new SolidColorBrush(Colors.White);
        }
    }

    private void TabStations_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 0)
        {
            TabStationsUnderline.Visibility = Visibility.Collapsed;
            TabStations.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        }
    }

    private void TabStations_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.SelectedTabIndex = 0;
    }

    private void FavoritesTabPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 1)
        {
            TabFavoritesUnderline.Visibility = Visibility.Visible;
            TabFavorites.Foreground = new SolidColorBrush(Colors.White);
        }
    }

    private void FavoritesTabPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 1)
        {
            TabFavoritesUnderline.Visibility = Visibility.Collapsed;
            TabFavorites.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        }
    }

    private void TabFavorites_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 1)
        {
            TabFavoritesUnderline.Visibility = Visibility.Visible;
            TabFavorites.Foreground = new SolidColorBrush(Colors.White);
        }
    }

    private void TabFavorites_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 1)
        {
            TabFavoritesUnderline.Visibility = Visibility.Collapsed;
            TabFavorites.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        }
    }

    private void TabFavorites_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.SelectedTabIndex = 1;
    }

    private void HistoryTabPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 2)
        {
            TabHistoryUnderline.Visibility = Visibility.Visible;
            TabHistory.Foreground = new SolidColorBrush(Colors.White);
        }
    }

    private void HistoryTabPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 2)
        {
            TabHistoryUnderline.Visibility = Visibility.Collapsed;
            TabHistory.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        }
    }

    private void TabHistory_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 2)
        {
            TabHistoryUnderline.Visibility = Visibility.Visible;
            TabHistory.Foreground = new SolidColorBrush(Colors.White);
        }
    }

    private void TabHistory_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 2)
        {
            TabHistoryUnderline.Visibility = Visibility.Collapsed;
            TabHistory.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        }
    }

    private void TabHistory_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.SelectedTabIndex = 2;
    }

    private void AddButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        AddButton.Foreground = new SolidColorBrush(Colors.White);
    }

    private void AddButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        AddButton.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
    }

    private StationsView? _stationsView;
    private FavoritesView? _favoritesView;
    private HistoryView? _historyView;

    private void UpdateView()
    {
        if (_viewModel.SelectedTabIndex == 0)
        {
            if (_stationsView == null)
            {
                _stationsView = new StationsView
                {
                    Stations = _viewModel.Stations,
                    OnStationSelected = _viewModel.SelectStationCommand,
                    OnToggleFavorite = _viewModel.ToggleFavoriteCommand,
                    SearchText = _viewModel.SearchText
                };
                _stationsView.DataContext = _viewModel;
            }
            else
            {
                _stationsView.Stations = _viewModel.Stations;
                _stationsView.SearchText = _viewModel.SearchText;
            }
            ContentArea.Content = _stationsView;
        }
        else if (_viewModel.SelectedTabIndex == 1)
        {
            if (_favoritesView == null)
            {
                _favoritesView = new FavoritesView
                {
                    FavoriteStations = _viewModel.FavoriteStations,
                    OnStationSelected = _viewModel.SelectStationCommand,
                    OnRemoveFavorite = _viewModel.ToggleFavoriteCommand,
                    SearchText = _viewModel.SearchText
                };
                _favoritesView.DataContext = _viewModel;
            }
            else
            {
                _favoritesView.FavoriteStations = _viewModel.FavoriteStations;
                _favoritesView.SearchText = _viewModel.SearchText;
            }
            ContentArea.Content = _favoritesView;
        }
        else if (_viewModel.SelectedTabIndex == 2)
        {
            if (_historyView == null)
            {
                _historyView = new HistoryView
                {
                    PlayHistory = _viewModel.PlayHistory
                };
                _historyView.DataContext = _viewModel;
            }
            else
            {
                _historyView.PlayHistory = _viewModel.PlayHistory;
            }
            ContentArea.Content = _historyView;
        }
    }

    private void AddStationButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddStationDialog();
    }

    private void ViewModel_RequestAddStation(object? sender, EventArgs e)
    {
        ShowAddStationDialog();
    }

    private void ViewModel_RequestEditStation(object? sender, Models.Station station)
    {
        ShowEditStationDialog(station);
    }

    private void ViewModel_RequestViewMetadata(object? sender, Models.Station station)
    {
        ShowMetadataDialog(station);
    }

    private void ShowAddStationDialog()
    {
        var dialog = new Views.AddStationDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.TitleText) && !string.IsNullOrWhiteSpace(dialog.UrlText))
        {
            _viewModel.AddStation(dialog.TitleText, dialog.UrlText);
        }
    }

    private void ShowEditStationDialog(Models.Station station)
    {
        var dialog = new Views.EditStationDialog(station.Title, station.Stream ?? string.Empty)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.TitleText) && !string.IsNullOrWhiteSpace(dialog.UrlText))
        {
            _viewModel.UpdateStation(station, dialog.TitleText, dialog.UrlText);
        }
    }

    private void ShowMetadataDialog(Models.Station station)
    {
        string? format = null;
        Dictionary<string, string>? httpHeaders = null;

        if (_viewModel.CurrentStation == station && _viewModel.CurrentStation.Stream == station.Stream)
        {
            format = _viewModel.GetStreamFormat();
            httpHeaders = _viewModel.GetHTTPHeaders();
        }
        else if (station.Stream != null)
        {
            var url = station.Stream.ToLowerInvariant();
            if (url.Contains(".aac") || url.Contains(".aacp"))
                format = "AAC";
            else if (url.Contains(".mp3"))
                format = "MP3";
            else if (url.Contains(".ogg"))
                format = "OGG";
            else if (url.Contains(".m4a"))
                format = "M4A";
        }

        var dialog = new Views.MetadataDialog(
            station.Title,
            station.CurrentTrack ?? string.Empty,
            station.CurrentArtist ?? string.Empty,
            station.Stream ?? string.Empty,
            format,
            httpHeaders)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void UpdateTabStyles()
    {
        if (TabStations == null || TabFavorites == null || TabHistory == null ||
            TabStationsUnderline == null || TabFavoritesUnderline == null || TabHistoryUnderline == null)
            return;

        var inactiveColor = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 170, 170, 170));
        var activeColor = new SolidColorBrush(Colors.White);

        if (_viewModel.SelectedTabIndex == 0)
        {
            TabStations.Foreground = activeColor;
            TabStationsUnderline.Visibility = Visibility.Visible;
            if (!FavoritesTabPanel.IsMouseOver && !TabFavorites.IsMouseOver)
            {
                TabFavorites.Foreground = inactiveColor;
            }
            TabFavoritesUnderline.Visibility = Visibility.Collapsed;
            if (!HistoryTabPanel.IsMouseOver && !TabHistory.IsMouseOver)
            {
                TabHistory.Foreground = inactiveColor;
            }
            TabHistoryUnderline.Visibility = Visibility.Collapsed;
        }
        else if (_viewModel.SelectedTabIndex == 1)
        {
            TabFavorites.Foreground = activeColor;
            TabFavoritesUnderline.Visibility = Visibility.Visible;
            if (!StationsTabPanel.IsMouseOver && !TabStations.IsMouseOver)
            {
                TabStations.Foreground = inactiveColor;
            }
            TabStationsUnderline.Visibility = Visibility.Collapsed;
            if (!HistoryTabPanel.IsMouseOver && !TabHistory.IsMouseOver)
            {
                TabHistory.Foreground = inactiveColor;
            }
            TabHistoryUnderline.Visibility = Visibility.Collapsed;
        }
        else if (_viewModel.SelectedTabIndex == 2)
        {
            TabHistory.Foreground = activeColor;
            TabHistoryUnderline.Visibility = Visibility.Visible;
            if (!StationsTabPanel.IsMouseOver && !TabStations.IsMouseOver)
            {
                TabStations.Foreground = inactiveColor;
            }
            TabStationsUnderline.Visibility = Visibility.Collapsed;
            if (!FavoritesTabPanel.IsMouseOver && !TabFavorites.IsMouseOver)
            {
                TabFavorites.Foreground = inactiveColor;
            }
            TabFavoritesUnderline.Visibility = Visibility.Collapsed;
        }
    }
}
