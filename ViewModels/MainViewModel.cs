using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RadioPlayer.Models;
using RadioPlayer.Services;
using RadioPlayer.ViewModels.Commands;

namespace RadioPlayer.ViewModels;

public class MainViewModel : ViewModelBase
{
    public event EventHandler? RequestAddStation;
    public event EventHandler<Station>? RequestEditStation;
    public event EventHandler<Station>? RequestViewMetadata;

    private readonly StationService _stationService;
    private BassService? _bassService;
    private Station? _currentStation;
    private Station? _selectedStation;
    private string _currentTrack = string.Empty;
    private string _currentArtist = string.Empty;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isBuffering;
    private int _selectedTabIndex;
    private double _progress = 0.1;
    private double _volume = 0.5;
    private double _listeningTimeHours = 0;
    private double _sessionListeningTimeHours = 0;
    private DateTime _playStartTime;
    private DateTime _lastSaveTime = DateTime.MinValue;
    private Timer? _listeningTimer;
    private Timer? _connectionTimeoutTimer;
    private List<PlayHistoryItem> _playHistory = new();
    private List<string> _stationHistory = new();
    private int _currentHistoryIndex = -1;
    private string _searchText = string.Empty;
    private ObservableCollection<Station> _allStations = new();
    private bool _showConsole = false;
    private Timer? _searchDebounceTimer;

    public MainViewModel()
    {
        _stationService = new StationService();

        try
        {
            _bassService = new BassService();
            _bassService.OnMetadataReceived += OnMetadataReceived;
            _bassService.OnPlaybackStateChanged += OnPlaybackStateChanged;
            _bassService.OnReconnectRequired += OnReconnectRequired;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации Bass: {ex.Message}");
            System.Windows.MessageBox.Show($"Не удалось инициализировать аудио систему:\n{ex.Message}\n\nУбедитесь, что Bass.dll находится в папке с приложением.",
                "Ошибка инициализации", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }

        LoadStations();
        LoadSettings();

        foreach (var s in Stations)
        {
            s.IsCurrentStation = false;
            s.IsSelected = false;
        }
        foreach (var s in FavoriteStations)
        {
            s.IsCurrentStation = false;
            s.IsSelected = false;
        }
        _selectedStation = null;

        PlayCommand = new RelayCommand(_ => Play(), _ => !IsPlaying || IsPaused);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsPlaying || IsPaused);
        PauseCommand = new RelayCommand(_ => Pause(), _ => IsPlaying);
        PreviousCommand = new RelayCommand(_ => Previous(), _ => CanGoPrevious());
        NextCommand = new RelayCommand(_ => Next(), _ => CanGoNext());
        SelectStationCommand = new RelayCommand(SelectStation);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        VolumeChangedCommand = new RelayCommand(OnVolumeChanged);
        SaveStationsOrderCommand = new RelayCommand(_ => SaveStationsOrder());
        AddStationCommand = new RelayCommand(_ => RequestAddStation?.Invoke(this, EventArgs.Empty));
        PlayStationCommand = new RelayCommand(PlayStation);
        DeleteStationCommand = new RelayCommand(obj => DeleteStation(obj as Station));
        EditStationCommand = new RelayCommand(obj => EditStation(obj as Station));
        ViewMetadataCommand = new RelayCommand(obj => ViewMetadata(obj as Station));
    }

    public ObservableCollection<Station> Stations { get; } = new();
    public ObservableCollection<Station> FavoriteStations { get; } = new();
    public List<PlayHistoryItem> PlayHistory => _playHistory;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                DebounceSearch();
            }
        }
    }

    private void DebounceSearch()
    {
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new Timer(_ =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    FilterStations();
                }));
        }, null, TimeSpan.FromMilliseconds(300), System.Threading.Timeout.InfiniteTimeSpan);
    }


    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                OnPropertyChanged();
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateFavoriteStations();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }

    public string CurrentTrack
    {
        get => _currentTrack;
        set
        {
            _currentTrack = value;
            OnPropertyChanged();
        }
    }

    public string CurrentArtist
    {
        get => _currentArtist;
        set
        {
            _currentArtist = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            OnPropertyChanged();
        }
    }

    public bool IsBuffering
    {
        get => _isBuffering;
        set
        {
            _isBuffering = value;
            OnPropertyChanged();
        }
    }

    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand SelectStationCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand VolumeChangedCommand { get; }
    public ICommand SaveStationsOrderCommand { get; }
    public ICommand AddStationCommand { get; }
    public ICommand PlayStationCommand { get; }
    public ICommand DeleteStationCommand { get; }
    public ICommand EditStationCommand { get; }
    public ICommand ViewMetadataCommand { get; }

    public string ListeningTimeText
    {
        get
        {
            var totalMinutes = (int)(_listeningTimeHours * 60);
            var sessionMinutes = (int)(_sessionListeningTimeHours * 60);

            if (_listeningTimeHours < 1)
            {
                return $"Всего: {totalMinutes} мин | Сессия: {sessionMinutes} мин";
            }
            var totalHours = (int)_listeningTimeHours;
            var sessionHours = (int)_sessionListeningTimeHours;
            if (_sessionListeningTimeHours < 1)
            {
                return $"Всего: {totalHours} ч | Сессия: {sessionMinutes} мин";
            }
            return $"Всего: {totalHours} ч | Сессия: {sessionHours} ч";
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (_volume != value)
            {
                _volume = value;
                OnPropertyChanged();
                if (_bassService != null)
                {
                    _bassService.Volume = (float)value;
                }
                SaveSettings();
            }
        }
    }

    private void LoadStations()
    {
        var stations = _stationService.LoadStations();
        _allStations.Clear();
        Stations.Clear();
        for (int i = 0; i < stations.Count; i++)
        {
            var station = stations[i];
            if (!string.IsNullOrEmpty(station.Title))
            {
                station.Title = FixEncoding(station.Title);
            }
            if (!string.IsNullOrEmpty(station.Description))
            {
                station.Description = FixEncoding(station.Description);
            }
            station.IsFavorite = _stationService.IsFavorite(station.Stream);
            station.IsCurrentStation = false;
            station.IsSelected = false;
            station.Index = i + 1;
            _allStations.Add(station);
            Stations.Add(station);
        }
        _selectedStation = null;
        UpdateFavoriteStations();

        if (_currentStation != null && !string.IsNullOrEmpty(_currentStation.Stream))
        {
            var currentStream = _currentStation.Stream;
            foreach (var s in Stations)
            {
                if (!string.IsNullOrEmpty(s.Stream))
                {
                    s.IsCurrentStation = s.Stream == currentStream;
                }
            }
            foreach (var s in FavoriteStations)
            {
                if (!string.IsNullOrEmpty(s.Stream))
                {
                    s.IsCurrentStation = s.Stream == currentStream;
                }
            }
        }
    }

    private void LoadSettings()
    {
        var settings = _stationService.LoadSettings();
        _volume = settings.Volume;
        _showConsole = settings.ShowConsole;
        _listeningTimeHours = settings.ListeningTimeHours;
        _playHistory = settings.PlayHistory ?? new List<PlayHistoryItem>();

        if (_bassService != null)
        {
            _bassService.Volume = (float)_volume;
        }

        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(ShowConsole));
        OnPropertyChanged(nameof(ListeningTimeText));
        OnPropertyChanged(nameof(PlayHistory));
    }

    public void UpdateFavoriteStations()
    {
        var currentStream = _currentStation?.Stream;
        var allFavorites = _stationService.GetFavoriteStations(_allStations.ToList());

        var favoriteStreams = new HashSet<string>(allFavorites.Select(f => f.Stream ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)));
        var currentFavoriteStreams = new HashSet<string>(FavoriteStations.Select(f => f.Stream ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)));

        if (favoriteStreams.SetEquals(currentFavoriteStreams) && FavoriteStations.Count == allFavorites.Count)
        {
            foreach (var station in FavoriteStations)
            {
                station.IsSelected = false;
                if (!string.IsNullOrEmpty(currentStream) && !string.IsNullOrEmpty(station.Stream))
                {
                    station.IsCurrentStation = station.Stream == currentStream;
                }
                else
                {
                    station.IsCurrentStation = false;
                }
            }
            return;
        }

        FavoriteStations.Clear();

        foreach (var station in allFavorites)
        {
            station.IsFavorite = true;
            station.IsSelected = false;
            if (!string.IsNullOrEmpty(currentStream) && !string.IsNullOrEmpty(station.Stream))
            {
                station.IsCurrentStation = station.Stream == currentStream;
            }
            else
            {
                station.IsCurrentStation = false;
            }
            FavoriteStations.Add(station);
        }

        foreach (var station in Stations)
        {
            station.IsFavorite = _stationService.IsFavorite(station.Stream);
        }
    }

    private void FilterStations()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            if (Stations.Count != _allStations.Count)
            {
                Stations.Clear();
                foreach (var station in _allStations)
                {
                    Stations.Add(station);
                }
            }
        }
        else
        {
            var searchText = _searchText.AsSpan();
            var matchingStations = new List<Station>(_allStations.Count);

            foreach (var station in _allStations)
            {
                if (MatchesSearch(station, searchText))
                {
                    matchingStations.Add(station);
                }
            }

            Stations.Clear();
            foreach (var station in matchingStations)
            {
                Stations.Add(station);
            }
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => UpdateFavoriteStations()));
    }

    private static bool MatchesSearch(Station station, ReadOnlySpan<char> searchText)
    {
        if (searchText.IsEmpty)
            return true;

        var title = (station.Title ?? string.Empty).AsSpan();
        var stream = (station.Stream ?? string.Empty).AsSpan();

        if (ContainsIgnoreCase(title, searchText) || ContainsIgnoreCase(stream, searchText))
            return true;

        var description = (station.Description ?? string.Empty).AsSpan();
        var currentTrack = (station.CurrentTrack ?? string.Empty).AsSpan();
        var currentArtist = (station.CurrentArtist ?? string.Empty).AsSpan();

        return ContainsIgnoreCase(description, searchText) ||
               ContainsIgnoreCase(currentTrack, searchText) ||
               ContainsIgnoreCase(currentArtist, searchText);
    }

    private static bool ContainsIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
    {
        if (source.IsEmpty || value.IsEmpty || value.Length > source.Length)
            return false;

        return MemoryExtensions.Contains(source, value, StringComparison.OrdinalIgnoreCase);
    }

    public void SaveStationsOrder()
    {
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            Services.Logger.Log($"SaveStationsOrder: Skipped because search is active: '{_searchText}'");
            return;
        }

        Services.Logger.Log($"SaveStationsOrder: Saving order for {Stations.Count} stations");

        var stationsToSave = new List<Station>();
        for (int i = 0; i < Stations.Count; i++)
        {
            var station = Stations[i];
            station.Index = i + 1;
            if (!string.IsNullOrEmpty(station.Title))
            {
                station.Title = FixEncoding(station.Title);
            }
            if (!string.IsNullOrEmpty(station.Description))
            {
                station.Description = FixEncoding(station.Description);
            }
            stationsToSave.Add(station);
            Services.Logger.Log($"SaveStationsOrder: Station {i + 1}: '{station.Title}'");
        }

        try
        {
            _stationService.SaveStations(stationsToSave);
            Services.Logger.Log($"SaveStationsOrder: Successfully saved {stationsToSave.Count} stations");
        }
        catch (Exception ex)
        {
            Services.Logger.LogError("SaveStationsOrder: Failed to save stations", ex);
        }

        _allStations.Clear();
        foreach (var station in stationsToSave)
        {
            _allStations.Add(station);
        }
    }

    private void PlayStation(object? parameter)
    {
        if (parameter is Station station)
        {
            SelectStation(station);
        }
    }

    public void DeleteStation(Station? station = null)
    {
        var stationToDelete = station ?? _selectedStation;
        if (stationToDelete == null)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        Action action = () =>
        {
            try
            {
                if (_currentStation == stationToDelete)
                {
                    Stop();
                }

                stationToDelete.IsSelected = false;
                if (_selectedStation == stationToDelete)
                {
                    _selectedStation = null;
                }

                if (Stations.Contains(stationToDelete))
                {
                    Stations.Remove(stationToDelete);
                }

                if (_allStations.Contains(stationToDelete))
                {
                    _allStations.Remove(stationToDelete);
                }

                if (FavoriteStations.Contains(stationToDelete))
                {
                    FavoriteStations.Remove(stationToDelete);
                }

                for (int i = 0; i < Stations.Count; i++)
                {
                    Stations[i].Index = i + 1;
                }

                SaveStationsOrder();
                FilterStations();
            }
            catch (Exception ex)
            {
                Services.Logger.LogError("Error in DeleteStation", ex);
            }
        };

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    public void SelectStationForAction(Station station)
    {
        if (station == null)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        Action action = () =>
        {
            try
            {
                if (_selectedStation != null && _selectedStation != station)
                {
                    _selectedStation.IsSelected = false;
                }

                _selectedStation = station;
                station.IsSelected = true;
            }
            catch (Exception ex)
            {
                Services.Logger.LogError("Error in SelectStationForAction", ex);
            }
        };

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private void SelectStation(object? parameter)
    {
        if (parameter is Station station)
        {
            var selectedStream = station.Stream;

            if (string.IsNullOrEmpty(selectedStream))
            {
                return;
            }

            foreach (var s in Stations)
            {
                s.IsCurrentStation = false;
            }

            foreach (var s in FavoriteStations)
            {
                s.IsCurrentStation = false;
            }

            foreach (var s in Stations)
            {
                if (!string.IsNullOrEmpty(s.Stream) && s.Stream == selectedStream)
                {
                    s.IsCurrentStation = true;
                }
            }

            foreach (var s in FavoriteStations)
            {
                if (!string.IsNullOrEmpty(s.Stream) && s.Stream == selectedStream)
                {
                    s.IsCurrentStation = true;
                }
            }

            var oldCurrentStation = _currentStation;
            var isDifferentStation = _isPlaying && oldCurrentStation != null && oldCurrentStation.Stream != selectedStream;

            _currentStation = Stations.FirstOrDefault(s => s.Stream == selectedStream)
                              ?? FavoriteStations.FirstOrDefault(s => s.Stream == selectedStream)
                              ?? station;

            if (!string.IsNullOrEmpty(selectedStream))
            {
                if (_stationHistory.Count == 0 || (_currentHistoryIndex >= 0 && _currentHistoryIndex < _stationHistory.Count && _stationHistory[_currentHistoryIndex] != selectedStream))
                {
                    if (_currentHistoryIndex < _stationHistory.Count - 1)
                    {
                        _stationHistory.RemoveRange(_currentHistoryIndex + 1, _stationHistory.Count - _currentHistoryIndex - 1);
                    }
                    _stationHistory.Add(selectedStream);
                    _currentHistoryIndex = _stationHistory.Count - 1;

                    if (_stationHistory.Count > 100)
                    {
                        _stationHistory.RemoveAt(0);
                        _currentHistoryIndex--;
                    }
                }
                else if (_currentHistoryIndex < 0)
                {
                    _stationHistory.Add(selectedStream);
                    _currentHistoryIndex = 0;
                }
            }

            // Сразу показываем "Подключение..." при выборе новой станции
            CurrentTrack = "Подключение...";
            CurrentArtist = _currentStation.Title;

            if (isDifferentStation)
            {
                IsBuffering = true;
            }

            UpdateFavoriteStations();

            Play();
        }
    }

    private void ToggleFavorite(object? parameter)
    {
        if (parameter is Station station)
        {
            if (_stationService.IsFavorite(station.Stream))
            {
                _stationService.RemoveFromFavorites(station.Stream);
                station.IsFavorite = false;
            }
            else
            {
                _stationService.AddToFavorites(station.Stream);
                station.IsFavorite = true;
            }
            UpdateFavoriteStations();
        }
    }

    public void AddStation(string? title, string? url)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (_allStations.Any(s => s.Stream.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            System.Windows.MessageBox.Show("Станция с таким URL уже существует.", "Дубликат",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            // Исправляем кодировку названия перед добавлением
            var fixedTitle = FixEncoding(title);

            var newStation = new Station
            {
                Title = fixedTitle,
                Stream = url,
                Index = _allStations.Count > 0 ? _allStations.Max(s => s.Index) + 1 : 1,
                CurrentTrack = string.Empty,
                CurrentArtist = string.Empty
            };

            _allStations.Add(newStation);

            if (string.IsNullOrWhiteSpace(_searchText))
            {
                Stations.Add(newStation);
            }
            else
            {
                FilterStations();
            }

            SaveStationsOrder();
        }
        catch (Exception)
        {
            var fixedTitle = FixEncoding(title ?? "Ошибка");
            var errorStation = new Station
            {
                Title = fixedTitle,
                Stream = url ?? string.Empty,
                Index = _allStations.Count > 0 ? _allStations.Max(s => s.Index) + 1 : 1,
                CurrentTrack = "Ошибка",
                CurrentArtist = string.Empty
            };

            _allStations.Add(errorStation);

            if (string.IsNullOrWhiteSpace(_searchText))
            {
                Stations.Add(errorStation);
            }
            else
            {
                FilterStations();
            }
        }
    }

    private void Play()
    {
        if (_currentStation == null)
        {
            var firstStation = Stations.FirstOrDefault() ?? FavoriteStations.FirstOrDefault();
            if (firstStation != null)
            {
                SelectStation(firstStation);
                return;
            }

            System.Windows.MessageBox.Show("Станция не выбрана", "Ошибка",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (_bassService == null)
        {
            System.Windows.MessageBox.Show("Ошибка инициализации аудио. Проверьте наличие Bass.dll",
                "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        try
        {
            if (IsPaused && _bassService.IsPaused)
            {
                _bassService.Resume();
                IsPlaying = true;
                IsPaused = false;
                StartListeningTimer();
                return;
            }

            IsBuffering = true;
            CurrentTrack = "Подключение...";
            CurrentArtist = _currentStation.Title;

            if (_metadataUpdateTimer != null)
            {
                _metadataUpdateTimer.Dispose();
                _metadataUpdateTimer = null;
            }
            _pendingMetadata = null;
            _pendingMetadataUrl = null;

            Task.Run(async () =>
            {
                if (_currentStation == null || string.IsNullOrEmpty(_currentStation.Stream))
                {
                    Services.Logger.LogError("Play: Cannot play - station is null or stream is empty", null);
                    return;
                }
                Services.Logger.Log($"Play: Attempting to play station: {_currentStation.Title}, URL: {_currentStation.Stream}");
                var started = await _bassService.Play(_currentStation.Stream);
                Services.Logger.Log($"Play: BassService.Play returned: {started}");
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (started)
                        {
                            IsBuffering = false;
                            IsPlaying = true;
                            IsPaused = false;
                            StartListeningTimer();
                            Progress = 0.1;
                            StartConnectionTimeout();
                            Services.Logger.Log($"Play: UI updated, IsPlaying=true, IsBuffering=false for station: {_currentStation?.Title}");
                        }
                        else
                        {
                            Services.Logger.LogError($"Play: Failed to start playback for station: {_currentStation?.Title}");
                            IsBuffering = false;
                            IsPlaying = false;
                            IsPaused = false;
                            StopListeningTimer();
                            StopConnectionTimeout();
                            if (_currentStation != null)
                            {
                                CurrentTrack = _currentStation.Title;
                                CurrentArtist = string.Empty;
                            }
                            else
                            {
                                CurrentTrack = string.Empty;
                                CurrentArtist = string.Empty;
                            }
                        }
                    }));
                }
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка при воспроизведении: {ex.Message}",
                "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void Pause()
    {
        _bassService?.Pause();
        IsPlaying = false;
        IsPaused = true;
        StopListeningTimer();
    }

    private void Stop()
    {
        _bassService?.Stop();
        IsPlaying = false;
        IsPaused = false;
        StopListeningTimer();
        StopConnectionTimeout();
        CurrentTrack = string.Empty;
        CurrentArtist = string.Empty;
    }

    private ObservableCollection<Station> GetActiveList()
    {
        return SelectedTabIndex == 1 ? FavoriteStations : Stations;
    }

    private bool CanGoPrevious()
    {
        return GetActiveList().Count > 0;
    }

    private bool CanGoNext()
    {
        return GetActiveList().Count > 0;
    }

    private void Previous()
    {
        var list = GetActiveList();
        if (list.Count == 0) return;

        int currentIndex = -1;
        if (_currentStation != null)
        {
            currentIndex = list.IndexOf(list.FirstOrDefault(s => s.Stream == _currentStation.Stream)!);
        }

        int prevIndex = (currentIndex - 1 + list.Count) % list.Count;
        SelectStation(list[prevIndex]);
    }

    private void Next()
    {
        var list = GetActiveList();
        if (list.Count == 0) return;

        int currentIndex = -1;
        if (_currentStation != null)
        {
            currentIndex = list.IndexOf(list.FirstOrDefault(s => s.Stream == _currentStation.Stream)!);
        }

        int nextIndex = (currentIndex + 1) % list.Count;
        SelectStation(list[nextIndex]);
    }

    private void SelectStationInternal(Station station)
    {
        var selectedStream = station.Stream;

        if (string.IsNullOrEmpty(selectedStream))
        {
            return;
        }

        foreach (var s in Stations)
        {
            s.IsCurrentStation = false;
        }

        foreach (var s in FavoriteStations)
        {
            s.IsCurrentStation = false;
        }

        foreach (var s in Stations)
        {
            if (!string.IsNullOrEmpty(s.Stream) && s.Stream == selectedStream)
            {
                s.IsCurrentStation = true;
            }
        }

        foreach (var s in FavoriteStations)
        {
            if (!string.IsNullOrEmpty(s.Stream) && s.Stream == selectedStream)
            {
                s.IsCurrentStation = true;
            }
        }

        var oldCurrentStation = _currentStation;
        var isDifferentStation = _isPlaying && oldCurrentStation != null && oldCurrentStation.Stream != selectedStream;

        _currentStation = Stations.FirstOrDefault(s => s.Stream == selectedStream)
                          ?? FavoriteStations.FirstOrDefault(s => s.Stream == selectedStream)
                          ?? station;

        if (_currentStation == null)
            return;

        CurrentTrack = "Подключение...";
        CurrentArtist = _currentStation.Title;

        if (_metadataUpdateTimer != null)
        {
            _metadataUpdateTimer.Dispose();
            _metadataUpdateTimer = null;
        }
        _pendingMetadata = null;
        _pendingMetadataUrl = null;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateFavoriteStations();
        }), System.Windows.Threading.DispatcherPriority.Background);

        System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        if (isDifferentStation)
        {
            IsBuffering = true;
        }

        Play();
    }

    private System.Threading.Timer? _metadataUpdateTimer;
    private string? _pendingMetadata;
    private string? _pendingMetadataUrl;

    private void OnMetadataReceived(string metadata, string? url)
{
    Services.Logger.Log($"OnMetadataReceived called with metadata: '{metadata}' (empty: {string.IsNullOrEmpty(metadata)}), URL: {url}");

    if (_currentStation == null)
    {
        Services.Logger.Log($"OnMetadataReceived: No current station, ignoring metadata");
        return;
    }

    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(_currentStation.Stream) && url != _currentStation.Stream)
    {
        Services.Logger.Log($"OnMetadataReceived: Metadata from different URL ({url} vs {_currentStation.Stream}), ignoring");
        return;
    }

    _pendingMetadata = metadata;
    _pendingMetadataUrl = url;

    if (_metadataUpdateTimer != null)
    {
        _metadataUpdateTimer.Dispose();
    }

    _metadataUpdateTimer = new System.Threading.Timer(_ =>
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_currentStation == null || _pendingMetadata == null)
                        return;

                    if (!string.IsNullOrEmpty(_pendingMetadataUrl) && !string.IsNullOrEmpty(_currentStation.Stream) && _pendingMetadataUrl != _currentStation.Stream)
                    {
                        Services.Logger.Log($"OnMetadataReceived (delayed): Metadata from different URL, ignoring");
                        return;
                    }

                    var originalMetadata = _pendingMetadata.Trim();
                    Services.Logger.Log($"OnMetadataReceived (delayed): Processing original: '{originalMetadata}'");

                    if (string.IsNullOrEmpty(originalMetadata))
                    {
                        HandleEmptyMetadata();
                        return;
                    }

                    // ФИКС: FixEncoding ТОЛЬКО ОДИН РАЗ в начале
                    var fixedMetadata = FixEncoding(originalMetadata);
                    Services.Logger.Log($"Fixed metadata: '{fixedMetadata}'");

                    // Убираем StreamTitle= если есть
                    string cleanMetadata = fixedMetadata;
                    if (cleanMetadata.StartsWith("StreamTitle=", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanMetadata = cleanMetadata.Substring(12).Trim('\'', '"', ' ');
                    }

                    // ФИКС: Улучшенный парсинг. Сначала ищем основной разделитель (тире)
                    var dashRegex = new Regex(@"\s*[—–-]\s*");
                    var parts = dashRegex.Split(cleanMetadata)
                                         .Select(p => p.Trim())
                                         .Where(p => !string.IsNullOrWhiteSpace(p))
                                         .ToArray();

                    Services.Logger.Log($"Split parts by dash: [{string.Join(", ", parts)}]");

                    string track = string.Empty;
                    string artist = string.Empty;

                    if (parts.Length >= 2)
                    {
                        // Возвращаем как было: первая часть (крупно) в Track, вторая в Artist
                        track = parts[0];
                        artist = string.Join(" - ", parts.Skip(1));
                    }
                    else if (parts.Length == 1)
                    {
                        // Если нет тире, пробуем слэш как запасной вариант
                        var slashRegex = new Regex(@"\s*/\s*");
                        parts = slashRegex.Split(cleanMetadata)
                                          .Select(p => p.Trim())
                                          .Where(p => !string.IsNullOrWhiteSpace(p))
                                          .ToArray();

                        if (parts.Length >= 2)
                        {
                            track = parts[0];
                            artist = string.Join(" / ", parts.Skip(1));
                        }
                        else
                        {
                            track = parts[0];
                        }
                    }
                    else if (parts.Length == 1)
                    {
                        track = parts[0];  // Всё как track
                    }

                    // ФИКС: Trim и fix снова только если нужно (редко)
                    track = FixEncoding(track.Trim());
                    artist = FixEncoding(artist.Trim());

                    if (string.IsNullOrWhiteSpace(track))
                    {
                        HandleEmptyMetadata();
                        return;
                    }

                    Services.Logger.Log($"Parsed - Artist: '{artist}', Track: '{track}'");

                    CurrentTrack = track;
                    CurrentArtist = !string.IsNullOrWhiteSpace(artist) ? artist : string.Empty;

                    if (_currentStation != null)
                    {
                        _currentStation.CurrentTrack = CurrentTrack;
                        _currentStation.CurrentArtist = CurrentArtist;
                        _currentStation.IsCurrentStation = true;
                        var currentStream = _currentStation.Stream;
                        foreach (var s in Stations)
                        {
                            s.IsCurrentStation = !string.IsNullOrEmpty(s.Stream) && s.Stream == currentStream;
                        }
                        foreach (var s in FavoriteStations)
                        {
                            s.IsCurrentStation = !string.IsNullOrEmpty(s.Stream) && s.Stream == currentStream;
                        }
                        UpdateStationInList(_currentStation);
                        Services.Logger.Log($"Updated station '{_currentStation.Title}' with track: {track}");
                    }

                    AddToHistory(CurrentTrack, CurrentArtist);
                }
                finally
                {
                    _pendingMetadata = null;
                    _pendingMetadataUrl = null;
                }
            }), System.Windows.Threading.DispatcherPriority.Normal);  // ФИКС: Normal приоритет для UI
        }
    }, null, TimeSpan.FromMilliseconds(300), System.Threading.Timeout.InfiniteTimeSpan);
}

    // Вынес fallback в отдельный метод для чистоты
    private void HandleEmptyMetadata()
    {
        if (_currentStation != null && CurrentTrack == "Подключение...")
        {
            CurrentTrack = _currentStation.Title;
            CurrentArtist = string.Empty;
            Services.Logger.Log($"Empty metadata received, replacing 'Подключение...' with station title: {_currentStation.Title}");

            _currentStation.CurrentTrack = CurrentTrack;
            _currentStation.CurrentArtist = CurrentArtist;
            _currentStation.IsCurrentStation = true;
            var currentStream = _currentStation.Stream;
            foreach (var s in Stations)
            {
                s.IsCurrentStation = !string.IsNullOrEmpty(s.Stream) && s.Stream == currentStream;
            }
            foreach (var s in FavoriteStations)
            {
                s.IsCurrentStation = !string.IsNullOrEmpty(s.Stream) && s.Stream == currentStream;
            }
            UpdateStationInList(_currentStation);
        }
    }

    public Station? CurrentStation => _currentStation;

    private void AddToHistory(string track, string artist)
    {
        if (_currentStation == null || string.IsNullOrEmpty(track) || !_isPlaying || _isPaused) return;

        var historyItem = new PlayHistoryItem
        {
            StationTitle = EnsureUtf8(_currentStation.Title),
            TrackTitle = EnsureUtf8(track),
            Artist = EnsureUtf8(artist),
            PlayedAt = DateTime.Now
        };

        _playHistory.Add(historyItem);
        if (_playHistory.Count > 100)
        {
            _playHistory.RemoveAt(0);
        }

        OnPropertyChanged(nameof(PlayHistory));
        SaveSettings();
    }

    private string EnsureUtf8(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return text;
        }
    }

    private void UpdateStationInList(Station station)
    {
        var stationInList = Stations.FirstOrDefault(s => s.Stream == station.Stream);
        if (stationInList != null)
        {
            stationInList.CurrentTrack = station.CurrentTrack;
            stationInList.CurrentArtist = station.CurrentArtist;
            stationInList.IsCurrentStation = station.IsCurrentStation;
        }

        var stationInFavorites = FavoriteStations.FirstOrDefault(s => s.Stream == station.Stream);
        if (stationInFavorites != null)
        {
            stationInFavorites.CurrentTrack = station.CurrentTrack;
            stationInFavorites.CurrentArtist = station.CurrentArtist;
            stationInFavorites.IsCurrentStation = station.IsCurrentStation;
        }
    }

    private void StartListeningTimer()
    {
        StopListeningTimer();
        _playStartTime = DateTime.Now;
        _listeningTimer = new Timer(_ =>
        {
            if (!_isPlaying || _isPaused)
                return;

            var now = DateTime.Now;
            var elapsed = (now - _playStartTime).TotalMinutes / 60.0;

            if (elapsed > 0)
            {
                _listeningTimeHours += elapsed;
                _sessionListeningTimeHours += elapsed;
                _playStartTime = now;

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        OnPropertyChanged(nameof(ListeningTimeText));

                        if ((now - _lastSaveTime).TotalSeconds >= 30)
                        {
                            SaveSettings();
                            _lastSaveTime = now;
                        }
                    }));
                }
            }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void StopListeningTimer()
    {
        if (_playStartTime != default)
        {
            var now = DateTime.Now;
            var elapsed = (now - _playStartTime).TotalMinutes / 60.0;
            if (elapsed > 0 && !_isPaused)
            {
                _listeningTimeHours += elapsed;
                _sessionListeningTimeHours += elapsed;
                OnPropertyChanged(nameof(ListeningTimeText));
                SaveSettings();
                _lastSaveTime = now;
            }
        }

        _listeningTimer?.Dispose();
        _listeningTimer = null;
        _playStartTime = default;
    }

    private void StartConnectionTimeout()
    {
        StopConnectionTimeout();
        _connectionTimeoutTimer = new Timer(_ =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (CurrentTrack == "Подключение..." && _currentStation != null)
                    {
                        Services.Logger.Log($"Connection timeout: No metadata received after 5 seconds, showing station title: {_currentStation.Title}");
                        CurrentTrack = _currentStation.Title;
                        CurrentArtist = string.Empty;

                        if (_currentStation != null)
                        {
                            _currentStation.CurrentTrack = CurrentTrack;
                            _currentStation.CurrentArtist = CurrentArtist;
                            UpdateStationInList(_currentStation);
                        }
                    }
                    StopConnectionTimeout();
                }));
            }
        }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
    }

    private void StopConnectionTimeout()
    {
        _connectionTimeoutTimer?.Dispose();
        _connectionTimeoutTimer = null;
    }

    private void OnVolumeChanged(object? parameter)
    {
        if (parameter is double volume)
        {
            Volume = volume;
        }
    }

    public void SaveSettings()
    {
        var settings = _stationService.LoadSettings();
        settings.Volume = _volume;
        settings.ShowConsole = _showConsole;
        settings.ListeningTimeHours = _listeningTimeHours;
        settings.PlayHistory = _playHistory;
        _stationService.SaveSettings(settings);
    }

    public bool ShowConsole
    {
        get => _showConsole;
        set
        {
            if (_showConsole != value)
            {
                _showConsole = value;
                OnPropertyChanged();
                Services.Logger.SetConsoleVisible(value);
                SaveSettings();
            }
        }
    }

    private void OnPlaybackStateChanged(bool isPlaying)
    {
        IsPlaying = isPlaying;
        IsPaused = !isPlaying && (_bassService?.IsPaused ?? false);

        if (isPlaying)
        {
            IsBuffering = false;
            StartListeningTimer();
            StopConnectionTimeout();
        }
        else
        {
            StopListeningTimer();
            StopConnectionTimeout();
        }
    }

    private void OnReconnectRequired(string url)
    {
        if (_currentStation == null || _currentStation.Stream != url)
            return;

        Services.Logger.Log($"OnReconnectRequired: Reconnecting to {_currentStation.Title}");

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                CurrentTrack = "Переподключение...";
                CurrentArtist = _currentStation.Title;
                IsBuffering = true;

                Task.Run(async () =>
                {
                    Services.Logger.Log($"OnReconnectRequired: Attempting to reconnect to URL: {url}");
                    var started = await (_bassService?.Play(url) ?? Task.FromResult(false));
                    Services.Logger.Log($"OnReconnectRequired: Reconnect result: {started}");

                    if (dispatcher != null)
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (started)
                            {
                                IsPlaying = true;
                                IsPaused = false;
                                IsBuffering = false;
                                _playStartTime = DateTime.Now;
                                StartListeningTimer();
                                Progress = 0.1;
                                StartConnectionTimeout();
                            }
                            else
                            {
                                IsBuffering = false;
                                IsPlaying = false;
                                IsPaused = false;
                                StopListeningTimer();
                                StopConnectionTimeout();
                                if (_currentStation != null)
                                {
                                    CurrentTrack = _currentStation.Title;
                                    CurrentArtist = string.Empty;
                                }
                            }
                        }));
                    }
                });
            }));
        }
    }

    private void EditStation(Station? station)
    {
        if (station != null)
        {
            RequestEditStation?.Invoke(this, station);
        }
    }

    private void ViewMetadata(Station? station)
    {
        if (station != null)
        {
            RequestViewMetadata?.Invoke(this, station);
        }
    }

    public void UpdateStationTitle(Station station, string newTitle)
    {
        if (station == null || string.IsNullOrWhiteSpace(newTitle))
            return;

        // Исправляем кодировку названия перед обновлением
        station.Title = FixEncoding(newTitle.Trim());
        SaveStationsOrder();
        OnPropertyChanged(nameof(Stations));
    }

    public void UpdateStation(Station station, string newTitle, string newUrl)
    {
        if (station == null || string.IsNullOrWhiteSpace(newTitle) || string.IsNullOrWhiteSpace(newUrl))
            return;

        // Исправляем кодировку названия перед обновлением
        station.Title = FixEncoding(newTitle.Trim());
        station.Stream = newUrl.Trim();
        SaveStationsOrder();
        OnPropertyChanged(nameof(Stations));
    }

    public string? GetStreamFormat()
    {
        return _bassService?.GetStreamFormat();
    }

    public Dictionary<string, string> GetHTTPHeaders()
    {
        if (_bassService == null || _currentStation == null)
            return new Dictionary<string, string>();

        var handle = _bassService.GetCurrentStreamHandle();
        if (handle == 0)
            return new Dictionary<string, string>();

        return _bassService.GetAllHTTPHeaders(handle);
    }

private string FixEncoding(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;

    if (HasValidCyrillic(text) && !ContainsWrongLatinChars(text))
        return text;

    bool hasSuspiciousChars = Regex.IsMatch(text, "[ÐÒÍÃàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÞÊÀËÓŽÿ]");
    bool hasWrongLatin = ContainsWrongLatinChars(text);

    bool needsFix = hasSuspiciousChars || hasWrongLatin;

    if (!needsFix && !HasValidCyrillic(text))
    {
        foreach (char c in text)
        {
            if ((c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7) || c == 0x00DF)
            {
                needsFix = true;
                break;
            }
        }
    }

    if (!needsFix)
        return text;

    Services.Logger.Log($"FixEncoding: Detected wrong encoding in text: '{text}'");

    string? bestResult = null;
    int bestScore = -1;

    try
    {
        var bytes = Encoding.GetEncoding("windows-1252").GetBytes(text);
        var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed Windows-1252->1251: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with 1252->1251: {ex.Message}");
    }

    try
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
        var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed ISO-8859-1->1251: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with ISO->1251: {ex.Message}");
    }

    try
    {
        var cp866Bytes = Encoding.GetEncoding(866).GetBytes(text);
        var fixedText = Encoding.UTF8.GetString(cp866Bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed UTF8-as-CP866->UTF8: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with UTF8-as-CP866: {ex.Message}");
    }

    try
    {
        var bytes = Encoding.GetEncoding(866).GetBytes(text);
        var fixedText = Encoding.UTF8.GetString(bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed CP866->UTF8: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with CP866->UTF8: {ex.Message}");
    }

    try
    {
        var win1251Bytes = Encoding.GetEncoding("windows-1251").GetBytes(text);
        var fixedText = Encoding.UTF8.GetString(win1251Bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed UTF8-as-1251->UTF8: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with UTF8-as-1251: {ex.Message}");
    }

    try
    {
        var win1251 = Encoding.GetEncoding("windows-1251");
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            bytes[i] = (byte)(text[i] & 0xFF);
        }
        var fixedText = win1251.GetString(bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed direct->1251: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with direct->1251: {ex.Message}");
    }

    try
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);
        int score = CalculateEncodingScore(fixedText);
        if (score > bestScore)
        {
            bestScore = score;
            bestResult = fixedText;
        }
        if (HasValidCyrillic(fixedText) && !ContainsWrongLatinChars(fixedText) && score > 50)
        {
            Services.Logger.Log($"FixEncoding: Fixed UTF8->1251: '{text}' -> '{fixedText}' (score: {score})");
            return fixedText;
        }
    }
    catch (Exception ex)
    {
        Services.Logger.Log($"FixEncoding: Error with UTF8->1251: {ex.Message}");
    }

    if (bestResult != null && bestScore > 30)
    {
        Services.Logger.Log($"FixEncoding: Using best result (score: {bestScore}): '{text}' -> '{bestResult}'");
        return bestResult;
    }

    if (hasWrongLatin && (CountWrongChars(text) / (double)text.Length > 0.2))
    {
        try
        {
            var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
            var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);
            if (HasValidCyrillic(fixedText))
            {
                Services.Logger.Log($"FixEncoding: Force-fixed ISO->1251: '{text}' -> '{fixedText}'");
                return fixedText;
            }
        }
        catch { /* ignore */ }
    }

    Services.Logger.Log($"FixEncoding: Could not fix '{text}', returning original");
    return text;
}

private int CalculateEncodingScore(string text)
{
    if (string.IsNullOrEmpty(text)) return -1;

    int score = 0;
    int cyrillicCount = 0;
    int wrongCharCount = 0;
    int totalLetters = 0;

    foreach (char c in text)
    {
        if (char.IsLetter(c))
        {
            totalLetters++;
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))  // Cyrillic
            {
                cyrillicCount++;
                score += 10;
                if ("апинаксюшаалёна".Contains(char.ToLowerInvariant(c))) score += 5;
            }
            else if (c >= 0x20 && c < 0x7F)  // ASCII
            {
                score += 1;
            }
            else if ((c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7) || c == 0x00DF)
            {
                wrongCharCount++;
                score -= 5;
            }
        }
    }

    if (totalLetters == 0) return -1;

    if (cyrillicCount > 0 && wrongCharCount == 0) score += 50;
    if (wrongCharCount > totalLetters * 0.1) score -= 20;
    if (cyrillicCount / (double)totalLetters > 0.5) score += 30;  // >50% кириллицы — супер

    return score;
}

private int CountWrongChars(string text)
{
    int count = 0;
    foreach (char c in text)
    {
        if ((c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7) || c == 0x00DF)
            count++;
    }
    return count;
}

private bool ContainsWrongLatinChars(string text)
{
    if (string.IsNullOrEmpty(text)) return false;

    int suspiciousCount = CountWrongChars(text);
    int totalLetters = text.Count(char.IsLetter);

    return totalLetters > 0 && (double)suspiciousCount / totalLetters > 0.1;
}



    private bool LooksLikeWrongEncoding(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int wrongCharCount = 0;
        int totalChars = 0;

        foreach (char c in text)
        {
            if (char.IsLetter(c) || char.IsDigit(c))
            {
                totalChars++;
                if (c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7)
                {
                    wrongCharCount++;
                }
            }
        }

        if (totalChars == 0) return false;
        return (double)wrongCharCount / totalChars > 0.2;
    }

    private bool HasValidCyrillic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (char c in text)
        {
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
            {
                return true;
            }
        }
        return false;
    }

    private int CountCyrillicChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int count = 0;
        foreach (char c in text)
        {
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
            {
                count++;
            }
        }

        return count;
    }

    public void Dispose()
    {
        StopListeningTimer();
        StopConnectionTimeout();
        _searchDebounceTimer?.Dispose();
        SaveSettings();
        _bassService?.Dispose();
    }
}
