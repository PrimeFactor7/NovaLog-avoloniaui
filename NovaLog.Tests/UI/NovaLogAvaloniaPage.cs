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

        Application app;
        if (fileArg != null)
            app = Application.Launch(exePath, fileArg);
        else
            app = Application.Launch(exePath);

        var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("NovaLog Avalonia main window did not appear in time.");
        window.WaitUntilClickable(TimeSpan.FromSeconds(5));

        // Give Avalonia a moment to finish rendering
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
        return new NovaLogAvaloniaPage(app, automation, window);
    }

    private static string FindExePath()
    {
        // Walk up from test bin to find the main project's output
        var testBin = AppContext.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var exePath = Path.Combine(projectRoot, "NovaLog.Avalonia", "bin", "Debug", "net9.0", "NovaLog.Avalonia.exe");
        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"NovaLog.Avalonia.exe not found at: {exePath}. Build the main project first (dotnet build).");
        return exePath;
    }

    // ── Element lookup ────────────────────────────────────────────

    public AutomationElement? FindByName(string name)
        => _window.FindFirstDescendant(cf => cf.ByName(name));

    public AutomationElement? FindById(string automationId)
        => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    // ── Toolbar buttons (use AutomationProperties.Name) ─────────

    public AutomationElement OpenFileButton
        => FindByName("btnOpenFile")
           ?? throw new ElementNotFoundException("btnOpenFile");

    public AutomationElement OpenFolderButton
        => FindByName("btnOpenFolder")
           ?? throw new ElementNotFoundException("btnOpenFolder");

    public AutomationElement FollowButton
        => FindByName("btnFollow")
           ?? throw new ElementNotFoundException("btnFollow");

    public AutomationElement SourcesButton
        => FindByName("btnSources")
           ?? throw new ElementNotFoundException("btnSources");

    public AutomationElement ThemeButton
        => FindByName("btnTheme")
           ?? throw new ElementNotFoundException("btnTheme");

    // ── Status bar (use AutomationId so Name returns text) ──────

    public AutomationElement? StatusFileLabel => FindById("lblFile");
    public AutomationElement? StatusLinesLabel => FindById("lblLines");
    public AutomationElement? StatusFollowLabel => FindById("lblFollow");

    public string StatusFile => StatusFileLabel?.Name ?? "";
    public string StatusLines => StatusLinesLabel?.Name ?? "";
    public string StatusFollow => StatusFollowLabel?.Name ?? "";

    // ── Panels (use AutomationId) ───────────────────────────────

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

    // ── Actions ──────────────────────────────────────────────────

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

    /// <summary>Parse the line count from the status bar (e.g. "2 lines" → 2).</summary>
    public int ParseLineCount()
    {
        var text = StatusLines; // e.g. "1,234 lines" or "0 lines"
        var numPart = text.Split(' ')[0].Replace(",", "");
        return int.TryParse(numPart, out var n) ? n : 0;
    }

    /// <summary>Wait for a brief period to let the UI settle.</summary>
    public void WaitForUi(int ms = 500)
        => Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(ms));

    /// <summary>Get the window title.</summary>
    public string Title => _window.Title;

    /// <summary>Check if the application process is still running.</summary>
    public bool IsRunning => !_app.HasExited;

    /// <summary>Returns true if the main window (or log area) contains visible text matching the given substring.</summary>
    public bool LogContentContainsText(string substring)
    {
        var root = LogViewPanel ?? _window;
        var found = root.FindFirstDescendant(cf => cf.ByName(substring));
        if (found != null) return true;
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
