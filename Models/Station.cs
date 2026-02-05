using System.ComponentModel;

namespace RadioPlayer.Models;

public class Station : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _stream = string.Empty;
    private string _description = string.Empty;
    private string _image = string.Empty;
    private string? _currentTrack;
    private string? _currentArtist;
    private bool _isFavorite;
    private bool _isCurrentStation;
    private bool _isSelected;
    private int _index;

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Stream
    {
        get => _stream;
        set { _stream = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string Image
    {
        get => _image;
        set { _image = value; OnPropertyChanged(); }
    }

    public string? CurrentTrack
    {
        get => _currentTrack;
        set { _currentTrack = value; OnPropertyChanged(); }
    }

    public string? CurrentArtist
    {
        get => _currentArtist;
        set { _currentArtist = value; OnPropertyChanged(); }
    }

    public bool IsCurrentStation
    {
        get => _isCurrentStation;
        set { _isCurrentStation = value; OnPropertyChanged(); }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { _isFavorite = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


