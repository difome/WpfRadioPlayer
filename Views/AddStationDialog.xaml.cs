using System;
using System.Windows;

namespace RadioPlayer.Views;

public partial class AddStationDialog : Window
{
    public string? TitleText { get; private set; }
    public string? UrlText { get; private set; }

    public AddStationDialog()
    {
        InitializeComponent();
        Loaded += AddStationDialog_Loaded;
        UrlTextBox.KeyDown += UrlTextBox_KeyDown;
    }

    private void AddStationDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Left = Owner.Left + (Owner.Width - Width) / 2;
            Top = Owner.Top + (Owner.Height - Height) / 2;
        }
        TitleTextBox.Focus();
    }

    private void UrlTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AddButton_Click(sender, e);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
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

