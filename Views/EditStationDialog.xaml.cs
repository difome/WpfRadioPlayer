using System;
using System.Windows;
using System.Windows.Input;

namespace RadioPlayer.Views;

public partial class EditStationDialog : Window
{
    public string? TitleText { get; private set; }
    public string? UrlText { get; private set; }

    public EditStationDialog(string currentTitle, string currentUrl)
    {
        InitializeComponent();
        Loaded += EditStationDialog_Loaded;
        TitleTextBox.Text = currentTitle;
        UrlTextBox.Text = currentUrl;
        TitleTextBox.SelectAll();
    }

    private void EditStationDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Left = Owner.Left + (Owner.Width - Width) / 2;
            Top = Owner.Top + (Owner.Height - Height) / 2;
        }
        TitleTextBox.Focus();
    }

    private void TitleTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveButton_Click(sender, e);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        TitleText = TitleTextBox.Text?.Trim();
        UrlText = UrlTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(TitleText) || string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}

