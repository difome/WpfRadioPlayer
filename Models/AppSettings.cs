using System.Collections.Generic;

namespace RadioPlayer.Models;

public class AppSettings
{
    public List<string> FavoriteStations { get; set; } = new();
    public string? LastSelectedStation { get; set; }
    public double ListeningTimeHours { get; set; }
    public List<PlayHistoryItem> PlayHistory { get; set; } = new();
    public double Volume { get; set; } = 0.5;
    public bool ShowConsole { get; set; } = false;
    public double WindowWidth { get; set; } = 700;
    public double WindowHeight { get; set; } = 500;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
}

public class PlayHistoryItem
{
    public string StationTitle { get; set; } = string.Empty;
    public string TrackTitle { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public System.DateTime PlayedAt { get; set; }
}
