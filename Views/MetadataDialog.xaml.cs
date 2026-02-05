using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using RadioPlayer.ViewModels;

namespace RadioPlayer.Views;

public partial class MetadataDialog : Window
{
    public MetadataDialog(string stationTitle, string track, string artist, string url, string? format = null, Dictionary<string, string>? httpHeaders = null)
    {
        InitializeComponent();
        Loaded += MetadataDialog_Loaded;

        StationTitleText.Text = string.IsNullOrWhiteSpace(stationTitle) ? "(не указано)" : FixEncoding(stationTitle);
        TrackText.Text = string.IsNullOrWhiteSpace(track) ? "(не указано)" : FixEncoding(track);
        ArtistText.Text = string.IsNullOrWhiteSpace(artist) ? "(не указано)" : FixEncoding(artist);
        UrlText.Text = string.IsNullOrWhiteSpace(url) ? "(не указано)" : url;

        if (!string.IsNullOrWhiteSpace(format))
        {
            FormatLabel.Visibility = System.Windows.Visibility.Visible;
            FormatText.Visibility = System.Windows.Visibility.Visible;
            FormatText.Text = format;
        }

        if (httpHeaders != null && httpHeaders.Count > 0)
        {
            var headersText = new System.Text.StringBuilder();
            foreach (var header in httpHeaders.OrderBy(h => h.Key))
            {
                headersText.AppendLine($"{header.Key}: {header.Value}");
            }

            HeadersLabel.Visibility = System.Windows.Visibility.Visible;
            HeadersText.Visibility = System.Windows.Visibility.Visible;
            HeadersText.Text = headersText.ToString().TrimEnd();
        }
    }

    private void MetadataDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Left = Owner.Left + (Owner.Width - Width) / 2;
            Top = Owner.Top + (Owner.Height - Height) / 2;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private string FixEncoding(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (HasValidCyrillic(text) && !ContainsWrongLatinChars(text))
        {
            return text;
        }

        bool hasSuspiciousChars = Regex.IsMatch(text, "[ÐÒÍÃàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÞÊÀËÓ]");
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

        if (needsFix)
        {
            try
            {
                var bytes = Encoding.GetEncoding("windows-1252").GetBytes(text);
                var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);

                if (HasValidCyrillic(fixedText))
                {
                    return fixedText;
                }
            }
            catch { }

            try
            {
                var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
                var fixedText = Encoding.GetEncoding("windows-1251").GetString(bytes);

                if (HasValidCyrillic(fixedText))
                {
                    return fixedText;
                }
            }
            catch { }
        }

        return text;
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

    private bool ContainsWrongLatinChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int suspiciousCount = 0;
        int totalLetters = 0;

        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                totalLetters++;
                if ((c >= 0x00C0 && c <= 0x00FF && c != 0x00D7 && c != 0x00F7) ||
                    c == 0x00DF ||
                    (c >= 0x00C0 && c <= 0x01FF && c != 0x00D7 && c != 0x00F7))
                {
                    suspiciousCount++;
                }
            }
        }

        if (totalLetters == 0) return false;
        return (double)suspiciousCount / totalLetters > 0.1;
    }
}
