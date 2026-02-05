using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RadioPlayer.Models;

namespace RadioPlayer.Services;

public class StationService
{
    private const string StationsFile = "stations.json";
    private const string SettingsFile = "settings.json";

    public List<Station> LoadStations()
    {
        if (!File.Exists(StationsFile))
            return new List<Station>();

        var json = File.ReadAllText(StationsFile, System.Text.Encoding.UTF8);
        return JsonConvert.DeserializeObject<List<Station>>(json) ?? new List<Station>();
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFile))
            return new AppSettings();

        var json = File.ReadAllText(SettingsFile, System.Text.Encoding.UTF8);
        var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();

        if (settings.PlayHistory != null)
        {
            foreach (var item in settings.PlayHistory)
            {
                item.TrackTitle = FixEncoding(item.TrackTitle);
                item.Artist = FixEncoding(item.Artist);
                item.StationTitle = FixEncoding(item.StationTitle);
            }
        }

        return settings;
    }

    private string FixEncoding(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (IsLikelyMisencoded(text))
        {
            try
            {
                var windows1251 = System.Text.Encoding.GetEncoding(1251);
                var utf8 = System.Text.Encoding.UTF8;

                var bytes = windows1251.GetBytes(text);
                var fixedText = utf8.GetString(bytes);
                if (IsValidCyrillic(fixedText))
                {
                    return fixedText;
                }
            }
            catch { }

            try
            {
                var defaultEncoding = System.Text.Encoding.Default;
                var utf8 = System.Text.Encoding.UTF8;

                var bytes = defaultEncoding.GetBytes(text);
                var fixedText = utf8.GetString(bytes);
                if (IsValidCyrillic(fixedText))
                {
                    return fixedText;
                }
            }
            catch { }
        }

        return text;
    }

    private bool IsLikelyMisencoded(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int cyrillicCount = 0;
        int invalidCount = 0;

        foreach (char c in text)
        {
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
            {
                cyrillicCount++;
            }
            else if (c >= 0x0080 && c <= 0x00FF && c != 0x00A0)
            {
                invalidCount++;
            }
        }

        return invalidCount > 0 && (cyrillicCount == 0 || invalidCount > cyrillicCount / 2);
    }

    private bool IsValidCyrillic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int cyrillicCount = 0;
        foreach (char c in text)
        {
            if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0500 && c <= 0x052F))
            {
                cyrillicCount++;
            }
        }

        return cyrillicCount > 0;
    }

    public void SaveSettings(AppSettings settings)
    {
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(SettingsFile, json, System.Text.Encoding.UTF8);
    }

    public void AddToFavorites(string streamUrl)
    {
        var settings = LoadSettings();
        if (!settings.FavoriteStations.Contains(streamUrl))
        {
            settings.FavoriteStations.Add(streamUrl);
            SaveSettings(settings);
        }
    }

    public void RemoveFromFavorites(string streamUrl)
    {
        var settings = LoadSettings();
        settings.FavoriteStations.Remove(streamUrl);
        SaveSettings(settings);
    }

    public bool IsFavorite(string streamUrl)
    {
        var settings = LoadSettings();
        return settings.FavoriteStations.Contains(streamUrl);
    }

    public List<Station> GetFavoriteStations(List<Station> allStations)
    {
        var settings = LoadSettings();
        return allStations.Where(s => settings.FavoriteStations.Contains(s.Stream)).ToList();
    }

    public void SaveStations(List<Station> stations)
    {
        var json = JsonConvert.SerializeObject(stations, Formatting.Indented);
        File.WriteAllText(StationsFile, json, System.Text.Encoding.UTF8);
    }
}
