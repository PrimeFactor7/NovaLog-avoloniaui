using System;
using System.Diagnostics;
using System.IO;
using Avalonia;

namespace NovaLog.Avalonia;

sealed class Program
{
    public static readonly Stopwatch StartupStopwatch = new();

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash(e.ExceptionObject as Exception);
        };

        try
        {
            StartupStopwatch.Start();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw; // Re-throw to let OS handle the crash dialog if needed
        }
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"NovaLog_Crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var dump = $"[{DateTime.Now:O}] FATAL UNHANDLED EXCEPTION\r\n{ex.GetType().FullName}: {ex.Message}\r\n\r\nSTACK TRACE:\r\n{ex.StackTrace}";
            
            File.WriteAllText(logFile, dump);
            Console.WriteLine(dump);
        }
        catch
        {
            // Failsafe: if we can't write to disk, at least write to console
            Console.WriteLine("CRASH LOGGER FAILED. Original exception: " + ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
