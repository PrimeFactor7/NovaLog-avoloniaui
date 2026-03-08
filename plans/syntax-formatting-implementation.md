# Implementation Plan: Auto-Formatting for JSON & SQL in Grid Span Lines Mode

## Context
NovaLog Avalonia's Grid Mode + "Span Lines" already merges continuation lines into single tall rows. However, compact single-line JSON/SQL messages display as one long line instead of formatted multiline content. The spec (`plans/syntax-formatting.md`) requires auto pretty-printing JSON with `System.Text.Json`, auto-formatting SQL with keyword uppercasing and clause-level line breaks, plus a max row height cap to prevent scroll breakage.

Syntax highlighting already exists (JSON tokenizer, SQL regex, StackTrace regex). This plan adds **formatting expansion** — transforming compact messages into indented multiline text at grid-source-build time.

**Codebase**: `d:\dev\src\tools\NovaLog-avoloniaui`

---

## What Already Exists

| Feature | Status | Location |
|---------|--------|----------|
| JSON syntax highlighting | Complete | `JsonHighlightTokenizer.cs` (state machine, 7 token kinds) |
| SQL syntax highlighting | Complete | `LogLineRow.cs` (4 regex patterns: keyword/string/operator/number) |
| StackTrace highlighting | Complete | `LogLineRow.cs` (3 regex patterns: method/file/exception) |
| Generic highlights | Complete | GUIDs, URLs, IPs, hex, numbers with overlap detection |
| Flavor detection | Complete | `SyntaxResolver.cs` (Span-based, priority: StackTrace > JSON > SQL) |
| Multiline span mode | Complete | `GridSourceBuilder.MergeContinuations()` + `GridRowViewModel.SubLines` |
| GridMessageCell rendering | Complete | Custom-drawn cell, loops SubLines with per-flavor highlighting |
| Per-flavor enable/disable | Complete | `JsonHighlightEnabled`, `SqlHighlightEnabled`, etc. in settings |
| Segmented control style | Complete | `SegmentedControl.axaml` (PrimeNG SelectButton clone) |
| Settings pattern | Complete | `AppSettings` → `SettingsViewModel` → `MainWindowViewModel` → workspace propagation |

---

## Architecture

### Data Flow
```
Log file → LogStreamer → LogLineParser → LogLine (raw)
  → LogLineViewModel (with SyntaxFlavor)
  → GridSourceBuilder.MergeContinuations()     ← existing: groups continuation lines
  → GridSourceBuilder.ApplyFormatting()         ← NEW: expands compact JSON/SQL
  → GridRowViewModel.FormattedLines             ← NEW: pretty-printed output
  → GridMessageCell.Render()                    ← renders FormattedLines with highlighting
```

### Key Design Decisions
1. **Formatting at build time, not render time** — determines row height for TreeDataGrid upfront
2. **`FormattedSubLine` (new type)** holds expanded text + flavor + continuation flag
3. **`FormattedLines` on GridRowViewModel** takes priority over `SubLines` in rendering
4. **Settings default to OFF** — user opts in to formatting
5. **Formatting only in Grid + Span Lines mode** — text mode renders lines as-is

---

## Phase 1: Core Formatter (NovaLog.Core — no UI deps)

### 1.1 Create `FormattedSubLine` model + `MessageFormatter` class
**File**: `NovaLog.Core/Services/MessageFormatter.cs` (NEW)

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// A single line of formatted output with its syntax flavor preserved.
/// </summary>
public sealed class FormattedSubLine
{
    public string Text { get; init; } = "";
    public SyntaxFlavor Flavor { get; init; }
    public bool IsContinuation { get; init; }
}

/// <summary>
/// Expands compact JSON/SQL messages into pretty-printed multi-line text.
/// Pure logic, no UI dependencies. Used by GridSourceBuilder at source-build time.
/// </summary>
public static class MessageFormatter
{
    /// <summary>
    /// Attempts to format the merged text of a row.
    /// Returns null if no formatting was applied (pass-through).
    /// </summary>
    public static List<FormattedSubLine>? Format(
        string mergedText,
        SyntaxFlavor flavor,
        bool jsonFormatEnabled,
        bool sqlFormatEnabled,
        int indentSize = 2,
        int maxLines = 50)
    {
        if (string.IsNullOrWhiteSpace(mergedText)) return null;

        return flavor switch
        {
            SyntaxFlavor.Json when jsonFormatEnabled => FormatJson(mergedText, indentSize, maxLines),
            SyntaxFlavor.Sql when sqlFormatEnabled => FormatSql(mergedText, indentSize, maxLines),
            _ => null
        };
    }

    /// <summary>Pretty-print JSON with System.Text.Json.</summary>
    public static List<FormattedSubLine>? FormatJson(string text, int indentSize, int maxLines)
    {
        // 1. Find first '{' or '[' — text before it is the prefix
        int braceIdx = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' || text[i] == '[') { braceIdx = i; break; }
        }
        if (braceIdx < 0) return null;

        string prefix = braceIdx > 0 ? text[..braceIdx] : "";
        string jsonPart = text[braceIdx..];

        // 2. Try parse with System.Text.Json
        try
        {
            using var doc = JsonDocument.Parse(jsonPart);

            // 3. Re-serialize with WriteIndented
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                IndentSize = indentSize,    // .NET 9+ property
            };
            string formatted = JsonSerializer.Serialize(doc.RootElement, options);

            // 4. Split into lines
            var rawLines = formatted.Split('\n');
            var result = new List<FormattedSubLine>(rawLines.Length);

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i].TrimEnd('\r');
                if (i == 0 && prefix.Length > 0)
                    line = prefix + line;

                result.Add(new FormattedSubLine
                {
                    Text = line,
                    Flavor = SyntaxFlavor.Json,
                    IsContinuation = i > 0,
                });
            }

            // 5. Truncate if needed
            return TruncateLines(result, maxLines);
        }
        catch
        {
            return null; // Invalid JSON — pass through raw text
        }
    }

    // SQL clause keywords that trigger a newline
    private static readonly Regex SqlClausePattern = new(
        @"\b(SELECT|FROM|WHERE|INNER\s+JOIN|LEFT\s+JOIN|RIGHT\s+JOIN|CROSS\s+JOIN|FULL\s+JOIN|JOIN|GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|UNION|SET|VALUES|ON|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqlKeywordUpperPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|INNER|LEFT|RIGHT|CROSS|FULL|OUTER|ON|AND|OR|NOT|IN|EXISTS|BETWEEN|LIKE|IS|NULL|AS|SET|VALUES|INTO|GROUP|BY|ORDER|ASC|DESC|HAVING|LIMIT|OFFSET|UNION|ALL|DISTINCT|COUNT|SUM|AVG|MIN|MAX|TOP|CASE|WHEN|THEN|ELSE|END|CREATE|ALTER|DROP|TABLE|INDEX|VIEW|EXEC|EXECUTE|DECLARE|BEGIN|COMMIT|ROLLBACK)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Format SQL: uppercase keywords, inject newlines before clauses, indent AND/OR.</summary>
    public static List<FormattedSubLine>? FormatSql(string text, int indentSize, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 1. Uppercase all SQL keywords
        string formatted = SqlKeywordUpperPattern.Replace(text, m => m.Value.ToUpperInvariant());

        // 2. Strip any existing newlines (normalize to single line first)
        formatted = formatted.Replace("\r\n", " ").Replace("\n", " ");

        // 3. Inject newlines before major clause keywords (but not the first one)
        formatted = SqlClausePattern.Replace(formatted, m =>
        {
            int idx = m.Index;
            // Don't add newline if this is at the very start
            if (idx == 0) return m.Value.ToUpperInvariant();
            return "\n" + m.Value.ToUpperInvariant();
        });

        // 4. Indent AND/OR lines
        string indent = new(' ', indentSize);
        formatted = Regex.Replace(formatted, @"\n\s*(AND|OR)\b", m =>
            "\n" + indent + m.Groups[1].Value.ToUpperInvariant(),
            RegexOptions.IgnoreCase);

        // 5. Split into lines
        var rawLines = formatted.Split('\n');
        var result = new List<FormattedSubLine>(rawLines.Length);

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            result.Add(new FormattedSubLine
            {
                Text = line,
                Flavor = SyntaxFlavor.Sql,
                IsContinuation = i > 0,
            });
        }

        // Don't format if it's already a single line (no expansion happened)
        if (result.Count <= 1) return null;

        return TruncateLines(result, maxLines);
    }

    /// <summary>Truncate lines at maxLines, adding an indicator for the rest.</summary>
    public static List<FormattedSubLine> TruncateLines(List<FormattedSubLine> lines, int maxLines)
    {
        if (maxLines <= 0 || lines.Count <= maxLines) return lines;

        int hidden = lines.Count - (maxLines - 1);
        var truncated = lines.GetRange(0, maxLines - 1);
        truncated.Add(new FormattedSubLine
        {
            Text = $"... ({hidden} more lines)",
            Flavor = SyntaxFlavor.None,
            IsContinuation = true,
        });
        return truncated;
    }
}
```

### 1.2 Unit tests
**File**: `NovaLog.Tests/Services/MessageFormatterTests.cs` (NEW)

Test cases:
- `FormatJson_CompactObject_PrettyPrints` — `{"a":1}` → 3 lines with braces and indented key
- `FormatJson_WithPrefix_PrefixOnFirstLine` — `userProfile{"id":1}` → first line starts with `userProfile{`
- `FormatJson_InvalidJson_ReturnsNull` — `not json {broken` → null
- `FormatJson_IndentSize4_Uses4Spaces` — verify 4-space indentation
- `FormatJson_ExceedsMaxLines_Truncates` — large JSON capped at maxLines with indicator
- `FormatJson_NestedObject_IndentsCorrectly` — nested `{"a":{"b":1}}` shows proper nesting
- `FormatSql_SimpleSelect_FormatsWithNewlines` — `SELECT a FROM b WHERE c=1` → 3 lines
- `FormatSql_KeywordsUppercased` — `select from where` → `SELECT FROM WHERE`
- `FormatSql_AndOrIndented` — `WHERE a=1 AND b=2 OR c=3` → AND/OR indented under WHERE
- `FormatSql_WithJoins_NewlineBeforeJoin` — JOIN clauses on separate lines
- `FormatSql_SingleClause_ReturnsNull` — `SELECT 1` stays single line → null (no expansion)
- `TruncateLines_UnderLimit_NoChange` — 5 lines with maxLines=10 → unchanged
- `TruncateLines_OverLimit_AddsIndicator` — 20 lines with maxLines=10 → 9 + indicator

---

## Phase 2: Grid Data Model + Builder Integration

### 2.1 Add `FormattedLines` to GridRowViewModel
**File**: `NovaLog.Avalonia/ViewModels/GridRowViewModel.cs`

```csharp
// Add using at top:
using NovaLog.Core.Services;

// Add after SubLines property:

/// <summary>
/// Formatted expansion lines (JSON pretty-print, SQL reformat).
/// When non-null, GridMessageCell renders these instead of SubLines.
/// </summary>
public List<FormattedSubLine>? FormattedLines { get; set; }

// Update LineCount:
public int LineCount => FormattedLines?.Count ?? SubLines?.Count ?? 1;
```

### 2.2 Add `FormattingOptions` record + `ApplyFormatting()` to GridSourceBuilder
**File**: `NovaLog.Avalonia/ViewModels/GridSourceBuilder.cs`

```csharp
// Add at top of file:
using NovaLog.Core.Services;

// New record (top-level or nested):
public sealed record FormattingOptions(
    bool JsonFormatEnabled,
    bool SqlFormatEnabled,
    int IndentSize = 2,
    int MaxRowLines = 50);
```

Update method signatures:
- `BuildFlat(lines, multiline, formatting?)` — add `FormattingOptions? formatting = null`
- `BuildHierarchical(lines, name, multiline, formatting?)` — same
- After `MergeContinuations()` returns, call `ApplyFormatting(result, formatting)` when formatting is non-null

New private method:
```csharp
private static void ApplyFormatting(IList<GridRowViewModel> rows, FormattingOptions options)
{
    foreach (var row in rows)
    {
        if (row.IsFileHeader || row.Line is null) continue;

        // Build merged text: join SubLines or use single message
        string text;
        SyntaxFlavor flavor;
        if (row.SubLines is { Count: > 0 } subLines)
        {
            text = string.Join("\n", subLines.Select(s => s.Message));
            flavor = subLines[0].Flavor;
        }
        else
        {
            text = row.Line.Message;
            flavor = row.Line.Flavor;
        }

        var formatted = MessageFormatter.Format(
            text, flavor,
            options.JsonFormatEnabled,
            options.SqlFormatEnabled,
            options.IndentSize,
            options.MaxRowLines);

        if (formatted is not null)
            row.FormattedLines = formatted;
    }
}
```

Also apply to hierarchical children in `FlushGroup()`:
```csharp
private static void FlushGroup(List<GridRowViewModel> result,
    ref GridRowViewModel? header, bool multiline = false,
    FormattingOptions? formatting = null)
{
    if (header is null) return;

    if (multiline && header.Children is { Count: > 0 } children)
    {
        var merged = MergeContinuations(children);
        children.Clear();
        foreach (var row in merged)
            children.Add(row);

        if (formatting is not null)
            ApplyFormatting(children, formatting);
    }

    header.ChildCount = header.Children?.Count ?? 0;
    result.Add(header);
    header = null;
}
```

---

## Phase 3: Cell Rendering

### 3.1 Update GridMessageCell
**File**: `NovaLog.Avalonia/Controls/GridMessageCell.cs`

`MeasureOverride` — already uses `row.LineCount` which will check `FormattedLines` first after Phase 2.1 changes. **No change needed.**

`Render()` — add FormattedLines check **before** SubLines check:
```csharp
// Add using at top:
using NovaLog.Core.Services;

// In Render(), after the IsFileHeader check:

// Formatted expansion (JSON pretty-print, SQL format)
if (row.FormattedLines is { Count: > 0 } fmtLines)
{
    for (int i = 0; i < fmtLines.Count; i++)
    {
        double y = TextY + i * R.RowHeight;
        var fl = fmtLines[i];
        RenderLine(context, fl.Text, fl.Flavor, fl.IsContinuation, y);
    }
    return;
}

// Multiline span mode: render each sub-line at its own Y offset
if (row.SubLines is { Count: > 0 } subLines)
// ... existing code ...
```

### 3.2 Timestamp/Level cells
**File**: `NovaLog.Avalonia/ViewModels/LogViewViewModel.cs`

Already use `row.LineCount` for `Height` calculation. Since `LineCount` now checks `FormattedLines` first, **no change needed.**

---

## Phase 4: Settings + Plumbing

### 4.1 AppSettings
**File**: `NovaLog.Core/Models/AppSettings.cs`

Add after existing grid settings:
```csharp
// Formatting
[JsonPropertyName("jsonFormatEnabled")]
public bool JsonFormatEnabled { get; set; }

[JsonPropertyName("sqlFormatEnabled")]
public bool SqlFormatEnabled { get; set; }

[JsonPropertyName("formatIndentSize")]
public int FormatIndentSize { get; set; } = 2;

[JsonPropertyName("maxRowLines")]
public int MaxRowLines { get; set; } = 50;

[JsonPropertyName("sectionFormattingExpanded")]
public bool SectionFormattingExpanded { get; set; }
```

### 4.2 SettingsViewModel
**File**: `NovaLog.Avalonia/ViewModels/SettingsViewModel.cs`

Add observable properties:
```csharp
// Formatting
[ObservableProperty] private bool _jsonFormatEnabled;
[ObservableProperty] private bool _sqlFormatEnabled;
[ObservableProperty] private int _formatIndentSize = 2;
[ObservableProperty] private int _maxRowLines = 50;

// Section
[ObservableProperty] private bool _sectionFormattingExpanded;
```

Add to `LoadFrom()`:
```csharp
JsonFormatEnabled = settings.JsonFormatEnabled;
SqlFormatEnabled = settings.SqlFormatEnabled;
FormatIndentSize = settings.FormatIndentSize;
MaxRowLines = settings.MaxRowLines;
SectionFormattingExpanded = settings.SectionFormattingExpanded;
```

Add to `SaveTo()`:
```csharp
settings.JsonFormatEnabled = JsonFormatEnabled;
settings.SqlFormatEnabled = SqlFormatEnabled;
settings.FormatIndentSize = FormatIndentSize;
settings.MaxRowLines = MaxRowLines;
settings.SectionFormattingExpanded = SectionFormattingExpanded;
```

### 4.3 IndentSizeToIndexConverter
**File**: `NovaLog.Avalonia/Converters/TabConverters.cs`

```csharp
/// <summary>Two-way converter: int indent size (2=index 0, 4=index 1) ↔ SelectedIndex.</summary>
public sealed class IndentSizeToIndexConverter : IValueConverter
{
    public static readonly IndentSizeToIndexConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 4 ? 1 : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 1 ? 4 : 2;
}
```

### 4.4 Settings UI — "Formatting" Expander
**File**: `NovaLog.Avalonia/Views/SettingsFlyout.axaml`

Add new Expander section between "Syntax Highlighting" and "Grid View":
```xml
<!-- Formatting -->
<Expander IsExpanded="{Binding SectionFormattingExpanded}">
  <Expander.Header>
    <StackPanel Orientation="Horizontal">
      <TextBlock Text="&#x2263;" Classes="expander-icon" />
      <TextBlock Text="Formatting" Classes="expander-header" />
    </StackPanel>
  </Expander.Header>
  <StackPanel Spacing="6" Margin="8,4">
    <TextBlock Text="Auto-format in Span Lines mode"
               FontSize="10" Foreground="{DynamicResource DimTextBrush}" />
    <CheckBox IsChecked="{Binding JsonFormatEnabled}" Content="Pretty-print JSON" />
    <CheckBox IsChecked="{Binding SqlFormatEnabled}" Content="Format SQL" />
    <Border Height="1" Background="{DynamicResource SeparatorBrush}" Margin="0,4" />
    <TextBlock Text="Indent Size" FontSize="11"
               Foreground="{DynamicResource DimTextBrush}" Margin="0,0,0,2" />
    <ListBox Classes="segmented" SelectionMode="Single"
             SelectedIndex="{Binding FormatIndentSize,
               Converter={x:Static vm:IndentSizeToIndexConverter.Instance}}">
      <ListBoxItem Content="2 spaces" />
      <ListBoxItem Content="4 spaces" />
    </ListBox>
    <DockPanel>
      <TextBlock DockPanel.Dock="Right"
                 Text="{Binding MaxRowLines}"
                 FontSize="11" Foreground="{DynamicResource AccentBrush}" />
      <TextBlock Text="Max row lines" FontSize="11"
                 Foreground="{DynamicResource DimTextBrush}" />
    </DockPanel>
    <Slider Minimum="10" Maximum="200"
            Value="{Binding MaxRowLines}"
            TickFrequency="10" IsSnapToTickEnabled="True" />
  </StackPanel>
</Expander>
```

### 4.5 Wire settings → grid rebuild

**File**: `NovaLog.Avalonia/ViewModels/LogViewViewModel.cs`
```csharp
// Add field:
private FormattingOptions? _formattingOptions;

// Add method:
public void SetFormattingOptions(FormattingOptions? options)
{
    _formattingOptions = options;
    if (IsGridMode) RebuildGridSource();
}

// Update RebuildGridSource() — pass formatting options:
private void RebuildGridSource()
{
    if (!IsGridMode) return;
    var fmtOpts = GridMultiline ? _formattingOptions : null;

    if (_memorySource is not null)
    {
        _gridRootRows = GridSourceBuilder.BuildHierarchical(
            _memorySource, Title, multiline: GridMultiline, formatting: fmtOpts);
        GridDataSource = CreateHierarchicalGridSource(_gridRootRows);
    }
    else if (_virtualSource is not null)
    {
        var rows = GridSourceBuilder.BuildFlat(
            _virtualSource, multiline: GridMultiline, formatting: fmtOpts);
        GridDataSource = CreateFlatGridSource(rows);
    }
}
```

**File**: `NovaLog.Avalonia/ViewModels/WorkspaceViewModel.cs`
```csharp
// Add field:
private FormattingOptions? _defaultFormattingOptions;

// Add method:
public void SetFormattingOptions(FormattingOptions? options, bool applyToExisting)
{
    _defaultFormattingOptions = options;
    if (!applyToExisting) return;
    foreach (var pane in GetAllPanes())
        pane.LogView.SetFormattingOptions(options);
}

// Update ApplyDefaultFollowState():
private void ApplyDefaultFollowState(PaneNodeViewModel pane)
{
    pane.LogView.IsFollowMode = _defaultMainFollow;
    pane.LogView.Filter.IsFollowMode = _defaultFilterFollow;
    pane.LogView.IsGridMode = _defaultGridMode;
    pane.LogView.GridMultiline = _defaultGridMultiline;
    pane.LogView.SetFormattingOptions(_defaultFormattingOptions);
}
```

**File**: `NovaLog.Avalonia/ViewModels/MainWindowViewModel.cs`
```csharp
// Add helper:
private FormattingOptions BuildFormattingOptions() => new(
    Settings.JsonFormatEnabled,
    Settings.SqlFormatEnabled,
    Settings.FormatIndentSize,
    Settings.MaxRowLines);

// In constructor, after existing grid setup:
Workspace.SetFormattingOptions(BuildFormattingOptions(), applyToExisting: true);

// In OnSettingsPropertyChanged, add cases:
case nameof(SettingsViewModel.JsonFormatEnabled):
case nameof(SettingsViewModel.SqlFormatEnabled):
case nameof(SettingsViewModel.FormatIndentSize):
case nameof(SettingsViewModel.MaxRowLines):
    Workspace.SetFormattingOptions(BuildFormattingOptions(), applyToExisting: true);
    break;
```

---

## Files Summary

| File | Action | Phase |
|------|--------|-------|
| `NovaLog.Core/Services/MessageFormatter.cs` | **New** — JSON pretty-print, SQL format, truncation | 1 |
| `NovaLog.Tests/Services/MessageFormatterTests.cs` | **New** — unit tests | 1 |
| `NovaLog.Avalonia/ViewModels/GridRowViewModel.cs` | Add `FormattedLines`, update `LineCount` | 2 |
| `NovaLog.Avalonia/ViewModels/GridSourceBuilder.cs` | Add `FormattingOptions` + `ApplyFormatting()` | 2 |
| `NovaLog.Avalonia/Controls/GridMessageCell.cs` | Render `FormattedLines` priority path | 3 |
| `NovaLog.Core/Models/AppSettings.cs` | Add formatting settings | 4 |
| `NovaLog.Avalonia/ViewModels/SettingsViewModel.cs` | Add observable properties + load/save | 4 |
| `NovaLog.Avalonia/Converters/TabConverters.cs` | Add `IndentSizeToIndexConverter` | 4 |
| `NovaLog.Avalonia/Views/SettingsFlyout.axaml` | Add "Formatting" Expander section | 4 |
| `NovaLog.Avalonia/ViewModels/LogViewViewModel.cs` | `SetFormattingOptions`, pass to builder | 4 |
| `NovaLog.Avalonia/ViewModels/WorkspaceViewModel.cs` | `SetFormattingOptions` propagation | 4 |
| `NovaLog.Avalonia/ViewModels/MainWindowViewModel.cs` | Wire formatting settings changes | 4 |

## Deferred (not in this plan)
- **JSON Interactive Tree** (spec §2 Method B) — complex separate feature
- **Stack trace path cleaning** (spec §4) — small but orthogonal, can be added later
- **Stack trace "External Code" grouping** (spec §4) — complex
- **File header navigation arrows** (spec §5) — separate feature
- **Global encoding/line-ending settings** (spec §1) — already UTF-8, edge case

## Verification
1. `dotnet build NovaLog.Avalonia` — clean build after each phase
2. `dotnet test NovaLog.Tests` — all tests pass including new MessageFormatterTests
3. Manual: Load logs with compact JSON → Settings > Formatting > enable "Pretty-print JSON" → Span Lines mode shows indented JSON with syntax highlighting
4. Manual: Load logs with SQL → enable "Format SQL" → SQL shows uppercase keywords with line breaks
5. Manual: Switch indent size 2→4 → JSON re-indents
6. Manual: Set max row lines to 10 → large JSON/SQL rows show truncation indicator
7. Manual: Disable formatting → rows revert to raw compact display
