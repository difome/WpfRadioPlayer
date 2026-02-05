using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using RadioPlayer.Services;

namespace RadioPlayer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Logger.Initialize();
        Logger.Log("Application started");

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            if (exception != null)
            {
                Logger.WriteCrashLog(exception, "Необработанное исключение в AppDomain");
                var logsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                MessageBox.Show(
                    $"Произошла критическая ошибка.\n\nКраш-лог сохранен в папке:\n{logsPath}\n\nПриложение будет закрыто.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        DispatcherUnhandledException += (sender, args) =>
        {
            Logger.WriteCrashLog(args.Exception, "Необработанное исключение в UI потоке");
            var logsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            var result = MessageBox.Show(
                $"Произошла ошибка.\n\nКраш-лог сохранен в папке:\n{logsPath}\n\nПродолжить работу приложения?",
                "Ошибка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);
            args.Handled = result == MessageBoxResult.Yes;
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Logger.WriteCrashLog(args.Exception, "Необработанное исключение в Task");
            args.SetObserved();
        };

        try
        {
            FontFamily? fontFamily = null;

            var paths = new[]
            {
                "pack://application:,,,/Fonts/otf/Inter-Regular.otf#Inter",
                "pack://application:,,,/Fonts/otf/Inter-Regular.otf",
                "pack://application:,,,/Fonts/static/Inter_24pt-Regular.ttf#Inter",
                "pack://application:,,,/Fonts/Inter-VariableFont_opsz,wght.ttf#Inter"
            };

            var extraBoldPaths = new[]
            {
                "pack://application:,,,/Fonts/otf/Inter-ExtraBold.otf#Inter",
                "pack://application:,,,/Fonts/otf/Inter-ExtraBold.otf"
            };

            foreach (var path in paths)
            {
                try
                {
                    fontFamily = new FontFamily(path);
                    Logger.Log($"Font Inter loaded successfully from: {path}");
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load font from {path}", ex);
                }
            }

            if (fontFamily != null)
            {
                Resources["InterFont"] = fontFamily;
            }
            else
            {
                throw new Exception("All font paths failed");
            }

            FontFamily? extraBoldFontFamily = null;
            foreach (var path in extraBoldPaths)
            {
                try
                {
                    extraBoldFontFamily = new FontFamily(path);
                    Logger.Log($"Font Inter ExtraBold loaded successfully from: {path}");
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load ExtraBold font from {path}", ex);
                }
            }

            if (extraBoldFontFamily != null)
            {
                Resources["InterExtraBoldFont"] = extraBoldFontFamily;
            }
            else
            {
                Resources["InterExtraBoldFont"] = fontFamily;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to load Inter font", ex);
            try
            {
                Resources["InterFont"] = new FontFamily("Inter");
                Logger.Log("Using system Inter font");
            }
            catch
            {
                Resources["InterFont"] = new FontFamily("Segoe UI");
                Logger.Log("Using Segoe UI as fallback");
            }
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
    }
}
