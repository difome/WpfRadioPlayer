using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using RadioPlayer.ViewModels;

namespace RadioPlayer.Views;

public partial class AboutDialog : Window
{
    public AboutDialog(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += AboutDialog_Loaded;
    }

    private void AboutDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Left = Owner.Left + (Owner.Width - Width) / 2;
            Top = Owner.Top + (Owner.Height - Height) / 2;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            VersionText.Text = $"Версия {version.Major}.{version.Minor}.{version.Build}";
        }
        else
        {
            VersionText.Text = "Версия 1.0.0";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AuthorLink_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/difome",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
