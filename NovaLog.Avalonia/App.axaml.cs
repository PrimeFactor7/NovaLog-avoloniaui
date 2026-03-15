using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NovaLog.Avalonia.Services;
using NovaLog.Avalonia.ViewModels;
using NovaLog.Avalonia.Views;
using NovaLog.Core.Services;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NovaLog.Avalonia;

public partial class App : Application
{
    private ThemeProxyManager? _themeProxyManager;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Pre-warm critical paths to avoid first-file JIT/allocation delays
            Task.Run(() => PreWarmCodePaths());

            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            // Support command-line file/folder loading
            if (desktop.Args is { Length: > 0 } && !string.IsNullOrWhiteSpace(desktop.Args[0]))
            {
                vm.LoadPath(desktop.Args[0]);
            }

            // Start theme proxy sidecar on Windows (job-bound so it exits with this process)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _themeProxyManager = new ThemeProxyManager();
                _themeProxyManager.StartProxy(AppDomain.CurrentDomain.BaseDirectory);
                desktop.Exit += (_, _) => _themeProxyManager?.StopProxy();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Pre-warms critical code paths to eliminate first-file loading delays
    /// caused by JIT compilation and first-time allocations.
    /// </summary>
    private static void PreWarmCodePaths()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine("[PREWARM] Starting pre-warm");

            // Create temp file for warm-up
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "2024-01-01 12:00:00.000 info: \tTest log line\n");

            try
            {
                // Warm up LogLineParser (regex compilation, etc.)
                System.Diagnostics.Debug.WriteLine($"[PREWARM] Warming LogLineParser ({sw.ElapsedMilliseconds}ms)");
                _ = LogLineParser.Parse("2024-01-01 12:00:00.000 info: \tTest", 0);

                // Warm up BigFileLineIndex and BigFileLogProvider
                System.Diagnostics.Debug.WriteLine($"[PREWARM] Warming BigFileLogProvider ({sw.ElapsedMilliseconds}ms)");
                using (var provider = new BigFileLogProvider(tempFile))
                {
                    provider.Open();
                    _ = provider.GetLine(0);
                }

                // Warm up LogStreamer
                System.Diagnostics.Debug.WriteLine($"[PREWARM] Warming LogStreamer ({sw.ElapsedMilliseconds}ms)");
                var streamer = new LogStreamer([tempFile]);
                _ = streamer.LoadHistory();
                streamer.Dispose();

                System.Diagnostics.Debug.WriteLine($"[PREWARM] Completed in {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PREWARM] Failed: {ex.Message}");
        }
    }
}
