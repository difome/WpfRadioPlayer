using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace RadioPlayer.Services;

public static class Logger
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static bool _consoleAllocated = false;

    public static void Initialize()
    {
        if (!_consoleAllocated)
        {
            AllocConsole();
            _consoleAllocated = true;

            for (int i = 0; i < 10; i++)
            {
                var consoleWindow = GetConsoleWindow();
                if (consoleWindow != IntPtr.Zero) break;
                System.Threading.Thread.Sleep(50);
            }

            Console.WriteLine("=== Radio Player Log ===");
            Console.WriteLine($"Started at: {DateTime.Now}");
            Console.WriteLine();
        }
    }

    public static void SetConsoleVisible(bool visible)
    {
        if (!_consoleAllocated && visible)
        {
            Initialize();
        }
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
        {
            ShowWindow(consoleWindow, visible ? SW_SHOW : SW_HIDE);
        }
    }

    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logLine = $"[{timestamp}] {message}";

        System.Diagnostics.Debug.WriteLine(logLine);

        if (_consoleAllocated)
        {
            Console.WriteLine(logLine);
        }
    }

    public static void LogMetadata(string source, string metadata)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logLine = $"[{timestamp}] [{source}] StreamTitle: {metadata}";

        System.Diagnostics.Debug.WriteLine(logLine);

        if (_consoleAllocated)
        {
            Console.WriteLine(logLine);
        }
    }

    public static void LogError(string message, Exception? ex = null)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logLine = $"[{timestamp}] ERROR: {message}";
        if (ex != null) logLine += $" (Ex: {ex.Message})";

        System.Diagnostics.Debug.WriteLine(logLine);

        if (_consoleAllocated)
        {
            Console.WriteLine(logLine);
            if (ex != null)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }
        }
    }

    public static void WriteCrashLog(Exception exception, string context = "")
    {
        try
        {
            var logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var crashLogPath = Path.Combine(logsDirectory, $"crash_{timestamp}.log");

            using var writer = new StreamWriter(crashLogPath, false, System.Text.Encoding.UTF8);

            writer.WriteLine("=== CRASH LOG ===");
            writer.WriteLine($"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"Контекст: {context}");
            writer.WriteLine();
            writer.WriteLine("=== ИСКЛЮЧЕНИЕ ===");
            writer.WriteLine($"Тип: {exception.GetType().FullName}");
            writer.WriteLine($"Сообщение: {exception.Message}");
            writer.WriteLine();
            writer.WriteLine("=== СТЕК ВЫЗОВОВ ===");
            writer.WriteLine(exception.StackTrace);
            writer.WriteLine();

            if (exception.InnerException != null)
            {
                writer.WriteLine("=== ВНУТРЕННЕЕ ИСКЛЮЧЕНИЕ ===");
                writer.WriteLine($"Тип: {exception.InnerException.GetType().FullName}");
                writer.WriteLine($"Сообщение: {exception.InnerException.Message}");
                writer.WriteLine($"Стек:");
                writer.WriteLine(exception.InnerException.StackTrace);
                writer.WriteLine();
            }

            writer.WriteLine("=== СИСТЕМНАЯ ИНФОРМАЦИЯ ===");
            writer.WriteLine($"OS: {Environment.OSVersion}");
            writer.WriteLine($"Версия .NET: {Environment.Version}");
            writer.WriteLine($"64-bit процесс: {Environment.Is64BitProcess}");
            writer.WriteLine($"64-bit ОС: {Environment.Is64BitOperatingSystem}");
            writer.WriteLine($"Рабочая директория: {Environment.CurrentDirectory}");
            writer.WriteLine($"Машинное имя: {Environment.MachineName}");
            writer.WriteLine($"Пользователь: {Environment.UserName}");

            LogError($"Краш-лог сохранен: {crashLogPath}", exception);
        }
        catch (Exception ex)
        {
            LogError($"Не удалось сохранить краш-лог", ex);
        }
    }
}
