using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Application = FlaUI.Core.Application;

namespace NovaLog.Tests.UI;

/// <summary>
/// Page Object Model for NovaLog Avalonia's main window.
/// Wraps FlaUI automation elements into a clean, reusable API.
/// </summary>
public sealed class NovaLogAvaloniaPage : IDisposable
{
    private static readonly object IsolatedHostLock = new();
    private static string? _isolatedHostDir;
    private static readonly HashSet<string> MutableHostFiles =
    [
        "workspace.json",
        "workspace.json.old",
        "novalog-settings.json"
    ];

    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public Window Window => _window;

    private NovaLogAvaloniaPage(Application app, UIA3Automation automation, Window window)
    {
        _app = app;
        _automation = automation;
        _window = window;
    }

    /// <summary>
    /// Launches NovaLog.Avalonia.exe and returns a page object for the main window.
    /// </summary>
    public static NovaLogAvaloniaPage Launch(string? exePath = null, string? fileArg = null)
    {
        exePath ??= FindExePath();
        exePath = PrepareIsolatedExe(exePath);

        Application app;
        if (fileArg != null)
            app = Application.Launch(exePath, fileArg);
        else
            app = Application.Launch(exePath);

        var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("NovaLog Avalonia main window did not appear in time.");
        window.WaitUntilClickable(TimeSpan.FromSeconds(5));

        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        return new NovaLogAvaloniaPage(app, automation, window);
    }

    private static string FindExePath()
    {
        var testBin = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var exePath = Path.Combine(projectRoot, "NovaLog.Avalonia", "bin", "Debug", "net10.0", "NovaLog.Avalonia.exe");
        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"NovaLog.Avalonia.exe not found at: {exePath}. Build the main project first (dotnet build).");
        return exePath;
    }

    private static string PrepareIsolatedExe(string builtExePath)
    {
        lock (IsolatedHostLock)
        {
            var sourceDir = Path.GetDirectoryName(builtExePath)
                ?? throw new InvalidOperationException("Could not determine Avalonia output directory.");

            _isolatedHostDir ??= Path.Combine(Path.GetTempPath(), $"NovaLogAvaloniaUiHost_{Guid.NewGuid():N}");
            CopyDirectory(sourceDir, _isolatedHostDir);

            ResetMutableState(_isolatedHostDir);
            return Path.Combine(_isolatedHostDir, Path.GetFileName(builtExePath));
        }
    }

    private static void ResetMutableState(string hostDir)
    {
        foreach (var fileName in MutableHostFiles)
        {
            var path = Path.Combine(hostDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        var logsDir = Path.Combine(hostDir, "logs");
        if (Directory.Exists(logsDir))
            Directory.Delete(logsDir, recursive: true);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    public AutomationElement? FindByName(string name)
        => _window.FindFirstDescendant(cf => cf.ByName(name));

    public AutomationElement? FindById(string automationId)
        => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    public AutomationElement FollowButton
        => FindById("btnFollow")
           ?? throw new ElementNotFoundException("btnFollow");

    public AutomationElement SourcesButton
        => FindById("btnSources")
           ?? throw new ElementNotFoundException("btnSources");

    public AutomationElement FilterButton
        => FindById("btnFilter")
           ?? throw new ElementNotFoundException("btnFilter");

    public AutomationElement ThemeButton
        => FindById("btnTheme")
           ?? throw new ElementNotFoundException("btnTheme");

    public AutomationElement? StatusFileLabel => FindById("lblFile");
    public AutomationElement? StatusLinesLabel => FindById("lblLines");
    public AutomationElement? StatusFollowLabel => FindById("lblFollow");

    public string StatusFile => StatusFileLabel?.Name ?? "";
    public string StatusLines => StatusLinesLabel?.Name ?? "";
    public string StatusFollow => StatusFollowLabel?.Name ?? "";

    public AutomationElement? SourceManagerPanel => FindById("sourceManagerPanel");
    public AutomationElement? LogViewPanel => FindById("logViewPanel");

    public bool IsSourceManagerVisible
    {
        get
        {
            var panel = SourceManagerPanel;
            return panel != null && !panel.IsOffscreen;
        }
    }

    public void ToggleSources()
    {
        SourcesButton.Click();
        WaitForUi(300);
    }

    public void ToggleFollow()
    {
        FollowButton.Click();
        WaitForUi(200);
    }

    public void ToggleTheme()
    {
        ThemeButton.Click();
        WaitForUi(200);
    }

    public int ParseLineCount()
    {
        var text = StatusLines;
        var numPart = text.Split(' ')[0].Replace(",", "");
        return int.TryParse(numPart, out var n) ? n : 0;
    }

    public void WaitForUi(int ms = 500)
        => Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(ms));

    public string Title => _window.Title;

    public bool IsRunning => !_app.HasExited;

    public bool LogContentContainsText(string substring)
    {
        var root = LogViewPanel ?? _window;
        var found = root.FindFirstDescendant(cf => cf.ByName(substring));
        if (found != null)
            return true;

        var all = root.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
        foreach (var el in all)
        {
            if (!string.IsNullOrEmpty(el.Name) && el.Name.Contains(substring, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        try { _app.Close(); } catch { }
        try { _app.Dispose(); } catch { }
        _automation.Dispose();
    }
}

public class ElementNotFoundException : Exception
{
    public ElementNotFoundException(string name) : base($"UI element '{name}' not found") { }
}

