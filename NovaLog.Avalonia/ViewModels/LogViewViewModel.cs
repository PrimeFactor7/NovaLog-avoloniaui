using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Avalonia.Controls;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using NovaLog.Core.Theme;
using AvDispatcher = global::Avalonia.Threading.Dispatcher;
using Avalonia.Data.Converters;
using System.Globalization;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// View model for a single log viewing pane. Always uses a list-based
/// items source (single code path for both small and large files).
/// </summary>
public partial class LogViewViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "No file loaded";
    [ObservableProperty] private bool _isFollowMode = true;
    [ObservableProperty] private bool _isLinked = true;
    [ObservableProperty] private int _totalLineCount;
    [ObservableProperty] private int? _selectedLineIndex;

    // ── Header metadata ─────────────────────────────────────────
    private int _fileCount;
    private int _sourceCount;
    private DateTime? _dateStart;
    private DateTime? _dateEnd;

    public string HeaderMeta => BuildHeaderMeta(compact: true);
    public string HeaderMetaFull => BuildHeaderMeta(compact: false);

    partial void OnTotalLineCountChanged(int value)
    {
        OnPropertyChanged(nameof(HeaderMeta));
        OnPropertyChanged(nameof(HeaderMetaFull));
    }

    private void RaiseHeaderMetaChanged()
    {
        OnPropertyChanged(nameof(HeaderMeta));
        OnPropertyChanged(nameof(HeaderMetaFull));
    }

    private string BuildHeaderMeta(bool compact)
    {
        if (TotalLineCount <= 0) return "";

        var parts = new List<string>(3);

        // File/source count
        if (_sourceCount > 0)
            parts.Add($"{_sourceCount} sources");
        else if (_fileCount > 1)
            parts.Add($"{_fileCount} files");

        // Date range
        if (_dateStart.HasValue && _dateEnd.HasValue)
        {
            if (compact)
            {
                // Short: "Mar 9 – Mar 11" or "Mar 9" if same day
                if (_dateStart.Value.Date == _dateEnd.Value.Date)
                    parts.Add(_dateStart.Value.ToString("MMM d"));
                else
                    parts.Add($"{_dateStart.Value:MMM d} – {_dateEnd.Value:MMM d}");
            }
            else
            {
                // Full: "2024-03-09 14:23 → 2024-03-11 09:15"
                parts.Add($"{_dateStart.Value:yyyy-MM-dd HH:mm} → {_dateEnd.Value:yyyy-MM-dd HH:mm}");
            }
        }
        else if (_dateStart.HasValue)
        {
            parts.Add(compact ? _dateStart.Value.ToString("MMM d") : _dateStart.Value.ToString("yyyy-MM-dd HH:mm"));
        }

        // Line count
        parts.Add($"{TotalLineCount:N0} lines");

        return string.Join(" · ", parts);
    }

    private void ExtractDateRange()
    {
        if (_memorySource is not null && _memorySource.Count > 0)
        {
            // Scan forward from start for first timestamp
            _dateStart = null;
            for (int i = 0; i < Math.Min(_memorySource.Count, 50); i++)
            {
                if (_memorySource[i].Timestamp is { } ts)
                {
                    _dateStart = ts;
                    break;
                }
            }
            // Scan backward from end for last timestamp
            _dateEnd = null;
            for (int i = _memorySource.Count - 1; i >= Math.Max(0, _memorySource.Count - 50); i--)
            {
                if (_memorySource[i].Timestamp is { } ts)
                {
                    _dateEnd = ts;
                    break;
                }
            }
        }
        else if (_provider is not null && _provider.LineCount > 0)
        {
            _dateStart = _provider.GetLine(0)?.Timestamp;
            _dateEnd = _provider.GetLine(_provider.LineCount - 1)?.Timestamp;
        }
        RaiseHeaderMetaChanged();
    }

    private int CountFileSeparators()
    {
        if (_memorySource is null) return 0;
        int count = 0;
        for (int i = 0; i < _memorySource.Count; i++)
        {
            if (_memorySource[i].IsFileSeparator)
                count++;
        }
        return count;
    }

    private bool _scrollPending;

    public event Action? SelectedLineChanged;
    public event Action? RowVisualsChanged;
    internal void NotifyRowVisualsChanged()
    {
        if (AvDispatcher.UIThread.CheckAccess())
            RowVisualsChanged?.Invoke();
        else
            AvDispatcher.UIThread.Post(() => RowVisualsChanged?.Invoke());
    }

    partial void OnSelectedLineIndexChanged(int? value)
    {
        SelectedLineChanged?.Invoke();
    }

    partial void OnIsFollowModeChanged(bool value)
    {
        if (value)
            RequestScrollToEndThrottled();
    }

    private void RequestScrollToEndThrottled()
    {
        if (_scrollPending) return;
        _scrollPending = true;

        void Fire()
        {
            _scrollPending = false;
            ScrollToEndRequested?.Invoke();
        }

        // When running in headless/test mode (no dispatcher), fire synchronously.
        if (AvDispatcher.UIThread.CheckAccess())
            Fire();
        else
            AvDispatcher.UIThread.Post(Fire, global::Avalonia.Threading.DispatcherPriority.Render);
    }

    [ObservableProperty] private bool _isGridMode = true;
    [ObservableProperty] private bool _gridMultiline = true;
    private FormattingOptions? _formattingOptions;
    [ObservableProperty] private bool _isIndexing;
    [ObservableProperty] private double _indexingProgress;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _navStatus = "";

    [RelayCommand]
    private void ToggleViewMode() => IsGridMode = !IsGridMode;

    [RelayCommand]
    private void ExpandAllFiles()
    {
        if (GridDataSource is global::Avalonia.Controls.HierarchicalTreeDataGridSource<GridRowViewModel> hs)
            hs.ExpandAll();
    }

    [RelayCommand]
    private void CollapseAllFiles()
    {
        if (GridDataSource is global::Avalonia.Controls.HierarchicalTreeDataGridSource<GridRowViewModel> hs)
            hs.CollapseAll();
    }

    partial void OnIsGridModeChanged(bool value)
    {
        if (value)
            RebuildGridSource();
        else
            GridDataSource = null;
    }

    partial void OnGridMultilineChanged(bool value)
    {
        if (IsGridMode)
            RebuildGridSource();
    }

    public void SetFormattingOptions(FormattingOptions? options)
    {
        _formattingOptions = options;
        if (IsGridMode)
            RebuildGridSource();
    }

    [ObservableProperty] private global::Avalonia.Controls.ITreeDataGridSource? _gridDataSource;

    private List<GridRowViewModel>? _gridRootRows;

    private void RebuildGridSource()
    {
        if (!IsGridMode) return;

        var fmt = GridMultiline ? _formattingOptions : null;

        if (_memorySource is not null)
        {
            _gridRootRows = GridSourceBuilder.BuildHierarchical(
                _memorySource, Title, multiline: GridMultiline, formatting: fmt);
            GridDataSource = CreateHierarchicalGridSource(_gridRootRows);
        }
        else if (_virtualSource is not null)
        {
            // Virtual sources use flat grid (too many lines for tree nodes)
            var rows = GridSourceBuilder.BuildFlat(_virtualSource, multiline: GridMultiline, formatting: fmt);
            GridDataSource = CreateFlatGridSource(rows);
        }
    }

    private static readonly global::Avalonia.Media.FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    private global::Avalonia.Controls.ITreeDataGridSource CreateFlatGridSource(
        IReadOnlyList<GridRowViewModel> rows)
    {
        return new global::Avalonia.Controls.FlatTreeDataGridSource<GridRowViewModel>(rows)
        {
            Columns =
            {
                new global::Avalonia.Controls.Models.TreeDataGrid.TemplateColumn<GridRowViewModel>(
                    "Timestamp", new global::Avalonia.Controls.Templates.FuncDataTemplate<GridRowViewModel>((row, _) =>
                        new global::Avalonia.Controls.TextBlock
                        {
                            Text = row?.TimestampText ?? "",
                            Foreground = LogLineRow.ResolveBrush("TimestampBrush") ?? LogLineRow.FallbackTimestamp,
                            FontFamily = MonoFont, FontSize = LogLineRow.LogFontSize,
                            Height = LogLineRow.RowHeight * (row?.LineCount ?? 1),
                            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                            Padding = new global::Avalonia.Thickness(0, (LogLineRow.RowHeight - LogLineRow.LogFontSize) / 2.0, 0, 0),
                            Margin = new global::Avalonia.Thickness(2, 0),
                        }, supportsRecycling: false),
                    width: new global::Avalonia.Controls.GridLength(180)),
                CreateLevelColumn(),
                CreateMessageColumn(),
            }
        };
    }

    private global::Avalonia.Controls.ITreeDataGridSource CreateHierarchicalGridSource(
        IReadOnlyList<GridRowViewModel> rootRows)
    {
        var source = new global::Avalonia.Controls.HierarchicalTreeDataGridSource<GridRowViewModel>(rootRows)
        {
            Columns =
            {
                new global::Avalonia.Controls.Models.TreeDataGrid.HierarchicalExpanderColumn<GridRowViewModel>(
                    new global::Avalonia.Controls.Models.TreeDataGrid.TemplateColumn<GridRowViewModel>(
                        "File / Timestamp", new global::Avalonia.Controls.Templates.FuncDataTemplate<GridRowViewModel>((row, _) =>
                        {
                            if (row is null) return new global::Avalonia.Controls.TextBlock();
                            if (row.IsFileHeader)
                            {
                                var label = string.IsNullOrEmpty(row.FileSizeText)
                                    ? $"{row.FileName}  ({row.ChildCount} lines)"
                                    : $"{row.FileName}  ({row.FileSizeText}, {row.ChildCount} lines)";
                                return new global::Avalonia.Controls.TextBlock
                                {
                                    Text = label,
                                    Foreground = LogLineRow.ResolveBrush("AccentBrush") ?? global::Avalonia.Media.Brushes.Cyan,
                                    FontFamily = MonoFont, FontSize = LogLineRow.LogFontSize,
                                    FontWeight = global::Avalonia.Media.FontWeight.Bold,
                                    Height = LogLineRow.RowHeight,
                                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                                    Margin = new global::Avalonia.Thickness(2, 0),
                                };
                            }
                            return new global::Avalonia.Controls.TextBlock
                            {
                                Text = row.TimestampText,
                                Foreground = LogLineRow.ResolveBrush("TimestampBrush") ?? LogLineRow.FallbackTimestamp,
                                FontFamily = MonoFont, FontSize = LogLineRow.LogFontSize,
                                Height = LogLineRow.RowHeight * row.LineCount,
                                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                                Padding = new global::Avalonia.Thickness(0, (LogLineRow.RowHeight - LogLineRow.LogFontSize) / 2.0, 0, 0),
                                Margin = new global::Avalonia.Thickness(2, 0),
                            };
                        }, supportsRecycling: false),
                        width: new global::Avalonia.Controls.GridLength(260)),
                    x => x.Children),
                CreateLevelColumn(),
                CreateMessageColumn(),
            }
        };

        // "Recent Only": collapse all, expand last file
        source.CollapseAll();
        if (rootRows.Count > 0)
            source.Expand(new global::Avalonia.Controls.IndexPath(rootRows.Count - 1));

        return source;
    }

    private static global::Avalonia.Controls.Models.TreeDataGrid.TemplateColumn<GridRowViewModel> CreateLevelColumn()
    {
        return new global::Avalonia.Controls.Models.TreeDataGrid.TemplateColumn<GridRowViewModel>(
            "Level", new global::Avalonia.Controls.Templates.FuncDataTemplate<GridRowViewModel>((row, _) =>
                new global::Avalonia.Controls.TextBlock
                {
                    Text = row?.LevelText ?? "",
                    Foreground = LogLineRow.ResolveLevelBrush(row?.Level ?? LogLevel.Unknown),
                    FontFamily = MonoFont, FontSize = LogLineRow.LogFontSize,
                    Height = LogLineRow.RowHeight * (row?.LineCount ?? 1),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                    Padding = new global::Avalonia.Thickness(0, (LogLineRow.RowHeight - LogLineRow.LogFontSize) / 2.0, 0, 0),
                    Margin = new global::Avalonia.Thickness(2, 0),
                }, supportsRecycling: false),
            width: new global::Avalonia.Controls.GridLength(70));
    }

    private static global::Avalonia.Controls.Models.TreeDataGrid.TemplateColumn<GridRowViewModel> CreateMessageColumn()
    {
        return new global::Avalonia.Controls.Models.TreeDataGrid.TemplateColumn<GridRowViewModel>(
            "Message", new global::Avalonia.Controls.Templates.FuncDataTemplate<GridRowViewModel>((_, _) =>
                new GridMessageCell(), supportsRecycling: false),
            width: new global::Avalonia.Controls.GridLength(1, global::Avalonia.Controls.GridUnitType.Star));
    }

    private readonly DelegatingItemsSource _delegatingSource = new();
    private InMemoryLogItemsSource? _memorySource;
    private VirtualLogItemsSource? _virtualSource;
    private IVirtualLogProvider? _provider;
    private Action<long>? _providerLinesAppended;
    private Action? _providerIndexingCompleted;
    private Action<long>? _providerIndexingProgressChanged;

    // Expose for FilterPanelViewModel to access the actual source
    internal InMemoryLogItemsSource? MemorySource => _memorySource;
    internal VirtualLogItemsSource? VirtualSource => _virtualSource;
    private LogStreamer? _streamer;
    private LogWatcher? _watcher;
    private GlobalClockService? _clock;
    private SourceManagerViewModel? _sourceManager;

    /// <summary>Tracks which source IDs are currently loaded in this pane (for clearing when source is removed).</summary>
    private readonly HashSet<string> _loadedSourceIds = new();

    /// <summary>Tracks the physical path(s) loaded in this pane as a fallback for tracking.</summary>
    private readonly HashSet<string> _loadedPaths = new();

    /// <summary>Gets the primary loaded source ID for persistence. Returns null if no source loaded.</summary>
    public string? GetLoadedSourceId()
    {
        if (_loadedSourceIds.Count > 0)
            return _loadedSourceIds.First();

        // Fallback: try to find source by path
        if (_loadedPaths.Count > 0 && _sourceManager != null)
        {
            var path = _loadedPaths.First();
            var source = _sourceManager.Sources.FirstOrDefault(s =>
                Path.GetFullPath(s.PhysicalPath).Equals(path, StringComparison.OrdinalIgnoreCase));
            return source?.SourceId;
        }

        return null;
    }

    /// <summary>Theme service for runtime color overrides.</summary>
    public ThemeService? Theme { get; private set; }

    /// <summary>
    /// Stable items source bound by the view. The object reference never changes;
    /// only its inner delegate is swapped, which fires CollectionChanged.Reset.
    /// </summary>
    public IEnumerable ItemsSource { get; }

    public FilterPanelViewModel Filter { get; } = new();
    public NavigationIndex NavIndex { get; } = new();
    public ObservableCollection<HighlightRule> HighlightRules { get; } = [];
    public PanePickerViewModel Picker { get; } = new();

    /// <summary>Active search matcher for highlighting matches in the log view. Set by FilterPanelViewModel.</summary>
    public CompiledMatcher? ActiveSearchMatcher { get; set; }

    public event Action? CloseRequested;
    
    [RelayCommand]
    private void ClosePane() => CloseRequested?.Invoke();

    public event Action? ScrollToEndRequested;
    /// <summary>Fired when the view should scroll to a specific line index.</summary>
    public event Action<int>? ScrollToLineRequested;

    /// <summary>Single placeholder line so the list is never empty and we can verify rendering.</summary>
    private static readonly IReadOnlyList<LogLineViewModel> PlaceholderLine = [
        new LogLineViewModel(new LogLine { GlobalIndex = 0, Message = "No file loaded — Open a file or use Gen to see logs.", RawText = "No file loaded" })
    ];

    public LogViewViewModel()
    {
        ItemsSource = _delegatingSource;
        _delegatingSource.SetInner(PlaceholderLine);
        TotalLineCount = 1;

        Filter.SearchHitsChanged += hits =>
        {
            NavIndex.SetSearchHits(hits);
        };
        Filter.BindTo(this);

        Picker.ItemSelected += item =>
        {
            if (item.Kind == SourceKind.File) LoadFile(item.Path);
            else if (item.Kind == SourceKind.Folder) LoadFolder(item.Path);
        };
    }

    public void Initialize(GlobalClockService clock, SourceManagerViewModel sourceManager, ThemeService theme)
    {
        _clock = clock;
        _sourceManager = sourceManager;
        Theme = theme;
    }

    public void ToggleFilter()
    {
        if (Filter.IsVisible)
            Filter.CloseCommand.Execute(null);
        else
            Filter.Show();
    }

    [RelayCommand]
    public void ShowPicker()
    {
        if (_sourceManager == null) return;

        var items = _sourceManager.Sources.Select(s => new PanePickerItem(
            s.SourceId, s.DisplayName, s.PhysicalPath, s.Kind, false));
        
        Picker.Show(items);
    }

    public void NavigateSearchHit(bool forward)
    {
        var target = NavIndex.GetNext(NavigationCategory.SearchHit, _currentLine, forward);
        if (target >= 0)
        {
            NavigateToLine((int)target);
            var (idx, total) = NavIndex.GetPositionInfo(NavigationCategory.SearchHit, target);
            NavStatus = $"Match {idx} of {total}";
        }
    }

    private int _currentLine;
    /// <summary>Set by the view to track the current viewport center line.</summary>
    public void SetCurrentLine(int line) => _currentLine = line;

    public void SelectLine(int line, bool disableFollow = true)
    {
        if (!TryNormalizeLineIndex(line, out var normalized))
            return;

        if (disableFollow)
            IsFollowMode = false;

        SelectedLineIndex = normalized;
        SetCurrentLine(normalized);
    }

    private bool TryNormalizeLineIndex(int requestedLine, out int normalizedLine)
    {
        var lineCount = GetLoadedLineCount();
        if (lineCount <= 0)
        {
            normalizedLine = -1;
            return false;
        }

        normalizedLine = Math.Clamp(requestedLine, 0, lineCount - 1);
        return true;
    }

    private int GetLoadedLineCount()
    {
        if (_memorySource is not null)
            return _memorySource.Count;

        if (_virtualSource is not null && _provider is not null)
            return _virtualSource.Count;

        return 0;
    }

    private bool TryGetActiveLineIndex(out int lineIndex)
    {
        var lineCount = GetLoadedLineCount();
        if (lineCount <= 0)
        {
            lineIndex = -1;
            return false;
        }

        if (SelectedLineIndex is int selectedLine && selectedLine >= 0 && selectedLine < lineCount)
        {
            lineIndex = selectedLine;
            return true;
        }

        if (_currentLine >= 0 && _currentLine < lineCount)
        {
            lineIndex = _currentLine;
            return true;
        }

        lineIndex = -1;
        return false;
    }

    private void ResetLineSelection()
    {
        SelectedLineIndex = null;
        _currentLine = 0;
    }

    /// <summary>Returns the timestamp of the current line, if any.</summary>
    public DateTime? GetCurrentTimestamp()
    {
        if (!TryGetActiveLineIndex(out var lineIndex))
            return null;

        if (_memorySource is not null)
            return _memorySource[lineIndex].Timestamp;
        if (_virtualSource is not null && _provider is not null)
            return _provider.GetLine(lineIndex)?.Timestamp;
        return null;
    }

    /// <summary>Seeks to the nearest line at or before the given timestamp.</summary>
    public void SeekToTimestamp(DateTime target)
    {
        if (_provider is not null)
        {
            _provider.ScrollToTimestamp(target, idx =>
            {
                NavigateToLine((int)idx);
            });
        }
        else if (_memorySource is not null && _memorySource.Count > 0)
        {
            // Binary search in memory
            int lo = 0, hi = _memorySource.Count - 1, best = 0;
            long targetTicks = target.Ticks;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var ts = _memorySource[mid].Timestamp;
                
                if (ts == null)
                {
                    int probe = mid - 1;
                    while (probe >= lo && _memorySource[probe].Timestamp == null) probe--;
                    if (probe >= lo)
                    {
                        ts = _memorySource[probe].Timestamp;
                        mid = probe;
                    }
                    else { lo = mid + 1; continue; }
                }

                if (ts is null) { lo = mid + 1; continue; }
                long midTicks = ts.Value.Ticks;
                if (midTicks < targetTicks) { best = mid; lo = mid + 1; }
                else if (midTicks > targetTicks) { hi = mid - 1; }
                else { best = mid; break; }
            }
            NavigateToLine(best);
        }
    }

    [RelayCommand]
    private void TimeTravel()
    {
        var ts = GetCurrentTimestamp();
        if (ts.HasValue)
        {
            _clock?.NotifyTimeChanged(ts.Value, this);
        }
    }

    /// <summary>Pins the current line's timestamp and broadcasts it to all linked panes.</summary>
    public void PinCurrentTimestamp()
    {
        var ts = GetCurrentTimestamp();
        if (ts.HasValue)
        {
            _clock?.NotifyTimeChanged(ts.Value, this);
        }
    }

    /// <summary>Returns the raw text of the current line, or null if unavailable.</summary>
    public string? GetCurrentLineText()
    {
        if (!TryGetActiveLineIndex(out var lineIndex))
            return null;

        if (_memorySource is not null)
            return _memorySource[lineIndex].RawText;
        if (_virtualSource is not null && _provider is not null)
            return _provider.GetRawLine(lineIndex);
        return null;
    }

    /// <summary>Extracts and formats JSON from the current line, or returns null if no valid JSON found.</summary>
    public string? GetFormattedJson()
    {
        var text = GetCurrentLineText();
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Try to find JSON in the line (look for { or [)
        int startBrace = text.IndexOf('{');
        int startBracket = text.IndexOf('[');
        int jsonStart = -1;

        if (startBrace >= 0 && startBracket >= 0)
            jsonStart = Math.Min(startBrace, startBracket);
        else if (startBrace >= 0)
            jsonStart = startBrace;
        else if (startBracket >= 0)
            jsonStart = startBracket;

        if (jsonStart < 0) return null;

        var jsonText = text.Substring(jsonStart);

        try
        {
            // Parse and reformat JSON
            using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Requests the view to scroll to a specific line.</summary>
    public void RequestScrollToLine(int line) => ScrollToLineRequested?.Invoke(line);

    public void NavigateToLine(int line)
    {
        if (!TryNormalizeLineIndex(line, out var normalized))
            return;

        IsFollowMode = false;
        SelectLine(normalized);
        RequestScrollToLine(normalized);
    }

    public void NavigateBookmark(bool forward)
    {
        var target = NavIndex.GetNext(NavigationCategory.Bookmark, _currentLine, forward);
        if (target >= 0)
        {
            NavigateToLine((int)target);
            var (idx, total) = NavIndex.GetPositionInfo(NavigationCategory.Bookmark, target);
            NavStatus = $"Bookmark {idx} of {total}";
        }
    }

    public void NavigateError(bool forward)
    {
        var target = NavIndex.GetNext(NavigationCategory.Error, _currentLine, forward);
        if (target >= 0)
        {
            NavigateToLine((int)target);
            var (idx, total) = NavIndex.GetPositionInfo(NavigationCategory.Error, target);
            NavStatus = $"Error {idx} of {total}";
        }
    }

    public void ToggleBookmark()
    {
        if (!TryGetActiveLineIndex(out var lineIndex))
            return;

        NavIndex.ToggleBookmark(lineIndex);
        var total = NavIndex.GetPositionInfo(NavigationCategory.Bookmark, lineIndex).TotalCount;
        NavStatus = total > 0 ? $"{total} Bookmarks" : "";
        RowVisualsChanged?.Invoke();
    }

    /// <summary>Gets a stable key for bookmark persistence (first loaded path).</summary>
    public string? GetBookmarkKey()
        => _loadedPaths.Count > 0 ? _loadedPaths.First() : null;

    /// <summary>Restores bookmarks from a saved list.</summary>
    public void RestoreBookmarks(List<long> bookmarks)
    {
        if (bookmarks.Count > 0)
            NavIndex.SetBookmarks(bookmarks);
    }

    /// <summary>Load lines from raw text (small file, all in memory).</summary>
    public void LoadFromLines(string filePath, IReadOnlyList<string> rawLines)
    {
        DisposeProvider();
        _loadedSourceIds.Clear();
        _loadedPaths.Clear();
        ResetLineSelection();

        _memorySource = new InMemoryLogItemsSource();
        var parsed = rawLines.Select((raw, i) => LogLineParser.Parse(raw, i));
        _memorySource.AddRange(parsed);

        _delegatingSource.SetInner(_memorySource);
        TotalLineCount = _memorySource.Count;
        Title = Path.GetFileName(filePath);

        // Compute header metadata
        _sourceCount = 0;
        _fileCount = CountFileSeparators();
        ExtractDateRange();

        // Track the physical path
        _loadedPaths.Add(Path.GetFullPath(filePath));
        // Also try to track by source ID if available
        TrackSourceByPath(filePath);

        // Notify filter that source changed
        Filter.OnSourceChanged(_memorySource);
        RebuildGridSource();

        if (IsFollowMode)
            RequestScrollToEndThrottled();
    }

    /// <summary>Load from a virtual provider (BigFile mode).</summary>
    public void LoadFromProvider(IVirtualLogProvider provider)
    {
        DisposeProvider();
        _loadedSourceIds.Clear();
        _loadedPaths.Clear();
        ResetLineSelection();

        _provider = provider;
        _virtualSource = new VirtualLogItemsSource(provider);

        _providerLinesAppended = _ =>
        {
            TotalLineCount = _virtualSource.Count;
            if (IsFollowMode)
                RequestScrollToEndThrottled();
        };
        provider.LinesAppended += _providerLinesAppended;

        _providerIndexingCompleted = () =>
        {
            IsIndexing = false;
            IndexingProgress = 1.0;
            TotalLineCount = _virtualSource.Count;
            ExtractDateRange();
        };
        provider.IndexingCompleted += _providerIndexingCompleted;

        _delegatingSource.SetInner(_virtualSource);
        TotalLineCount = _virtualSource.Count;
        Title = Path.GetFileName(provider.FilePath);

        // Metadata: single file, no file separators
        _sourceCount = 0;
        _fileCount = 0;
        ExtractDateRange();

        // Track the physical path
        _loadedPaths.Add(Path.GetFullPath(provider.FilePath));
        // Also try to track by source ID if available
        TrackSourceByPath(provider.FilePath);

        // Notify filter that source changed (virtual sources don't support incremental search yet)
        Filter.OnSourceChanged(null);
        RebuildGridSource();
    }

    private const long BigFileThreshold = 512 * 1024; // 512 KB - balance between indexing overhead and memory usage

    /// <summary>Load a file. Uses BigFileLogProvider (memory-mapped) for files > 512 KB, else reads into memory.</summary>
    public void LoadFile(string filePath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[LOAD] Start loading: {Path.GetFileName(filePath)}");

        if (BinaryDetector.IsBinary(filePath))
        {
            Title = $"Binary: {Path.GetFileName(filePath)}";
            return;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            System.Diagnostics.Debug.WriteLine($"[LOAD] File size: {fileInfo.Length:N0} bytes ({sw.ElapsedMilliseconds}ms)");

            if (fileInfo.Length > BigFileThreshold)
            {
                // Large file: use memory-mapped provider with indexing
                System.Diagnostics.Debug.WriteLine($"[LOAD] Using BigFileLogProvider ({sw.ElapsedMilliseconds}ms)");
                var provider = new BigFileLogProvider(filePath);

                System.Diagnostics.Debug.WriteLine($"[LOAD] Calling provider.Open() ({sw.ElapsedMilliseconds}ms)");
                provider.Open(); // Start indexing FIRST
                System.Diagnostics.Debug.WriteLine($"[LOAD] provider.Open() returned ({sw.ElapsedMilliseconds}ms)");

                LoadFromProvider(provider); // Sets up LinesAppended + IndexingCompleted

                // Add progress tracking on top (LoadFromProvider doesn't handle this)
                _providerIndexingProgressChanged = _ =>
                {
                    IndexingProgress = provider.IndexingProgress;
                    IsIndexing = true;
                };
                provider.IndexingProgressChanged += _providerIndexingProgressChanged;
                System.Diagnostics.Debug.WriteLine($"[LOAD] LoadFromProvider completed ({sw.ElapsedMilliseconds}ms)");
            }
            else
            {
                // Small file: read into memory (faster for files < 512KB, no indexing overhead)
                System.Diagnostics.Debug.WriteLine($"[LOAD] Using in-memory LogStreamer ({sw.ElapsedMilliseconds}ms)");
                var streamer = new LogStreamer([filePath]);
                System.Diagnostics.Debug.WriteLine($"[LOAD] Calling LoadHistory() ({sw.ElapsedMilliseconds}ms)");
                var rawLines = streamer.LoadHistory();
                System.Diagnostics.Debug.WriteLine($"[LOAD] LoadHistory returned {rawLines.Count} lines ({sw.ElapsedMilliseconds}ms)");
                LoadFromLines(filePath, rawLines);
                System.Diagnostics.Debug.WriteLine($"[LOAD] LoadFromLines completed ({sw.ElapsedMilliseconds}ms)");
                StartStreaming(streamer);
                System.Diagnostics.Debug.WriteLine($"[LOAD] StartStreaming completed ({sw.ElapsedMilliseconds}ms)");
            }
            System.Diagnostics.Debug.WriteLine($"[LOAD] Total load time: {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Title = $"Error: {Path.GetFileName(filePath)}";
            TotalLineCount = 0;
            _delegatingSource.SetInner(Array.Empty<LogLineViewModel>());
            System.Diagnostics.Debug.WriteLine($"LoadFile failed: {ex.Message}");
        }
    }

    public void LoadMerge(IEnumerable<SourceItemViewModel> sources)
    {
        var sourcesList = sources as IList<SourceItemViewModel> ?? sources.ToList();

        DisposeProvider();
        _loadedSourceIds.Clear();
        _loadedPaths.Clear();
        ResetLineSelection();

        var engine = new ChronoMergeEngine();
        int priority = 0;

        foreach (var src in sourcesList)
        {
            // Track each source ID in the merge
            _loadedSourceIds.Add(src.SourceId);
            // Also track physical paths
            if (!string.IsNullOrEmpty(src.PhysicalPath) && !src.PhysicalPath.StartsWith("merge://"))
                _loadedPaths.Add(Path.GetFullPath(src.PhysicalPath));

            LogStreamer streamer;
            if (src.Kind == SourceKind.Folder)
            {
                var auditManager = new AuditLogManager(src.PhysicalPath);
                auditManager.Refresh();
                var prefixMap = auditManager.GetPrefixToAuditMap();
                if (prefixMap.Count > 0)
                    streamer = new LogStreamer(auditManager, prefixMap.Values.First());
                else
                {
                    var logFiles = System.IO.Directory.GetFiles(src.PhysicalPath, "*.log").OrderBy(f => f).ToList();
                    if (logFiles.Count == 0)
                        logFiles = System.IO.Directory.GetFiles(src.PhysicalPath, "*.txt").OrderBy(f => f).ToList();
                    streamer = new LogStreamer(logFiles);
                }
            }
            else
            {
                streamer = new LogStreamer([src.PhysicalPath]);
            }

            int srcIdx = engine.AddSource(streamer, src.DisplayName, src.SourceColorHex, priority++);
            var history = streamer.LoadHistory();
            engine.PushHistory(srcIdx, history);
        }

        _providerIndexingProgressChanged = p => { IndexingProgress = engine.IndexingProgress; IsIndexing = true; };
        engine.IndexingProgressChanged += _providerIndexingProgressChanged;
        _providerIndexingCompleted = () => { IsIndexing = false; IndexingProgress = 1.0; TotalLineCount = (int)engine.LineCount; };
        engine.IndexingCompleted += _providerIndexingCompleted;
        _providerLinesAppended = _ => { TotalLineCount = (int)engine.LineCount; if (IsFollowMode) RequestScrollToEndThrottled(); };
        engine.LinesAppended += _providerLinesAppended;

        engine.Build();
        engine.StartTailing();

        _provider = engine;
        _virtualSource = new VirtualLogItemsSource(engine);
        _delegatingSource.SetInner(_virtualSource);
        TotalLineCount = (int)engine.LineCount;
        Title = $"Merged ({sourcesList.Count} sources)";
        IsStreaming = true;

        // Header metadata
        _sourceCount = sourcesList.Count;
        _fileCount = 0;
        ExtractDateRange();

        // Notify filter that source changed (merged sources use virtual provider)
        Filter.OnSourceChanged(null);
        RebuildGridSource();
    }

    /// <summary>Load a folder, discovering audit JSON or log files, with streaming.</summary>
    public void LoadFolder(string directoryPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] ====== Start loading: {directoryPath} ======");
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Full path: {Path.GetFullPath(directoryPath)}");
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Directory exists: {Directory.Exists(directoryPath)}");

        if (!Directory.Exists(directoryPath))
        {
            DisposeProvider();
            _loadedSourceIds.Clear();
            _loadedPaths.Clear();
            ResetLineSelection();
            _delegatingSource.SetInner(PlaceholderLine);
            Title = $"Missing: {Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
            TotalLineCount = PlaceholderLine.Count;
            Filter.OnSourceChanged(null);
            return;
        }

        DisposeProvider();

        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Creating AuditLogManager ({sw.ElapsedMilliseconds}ms)");
        var auditManager = new AuditLogManager(directoryPath);
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Calling auditManager.Refresh() ({sw.ElapsedMilliseconds}ms)");
        auditManager.Refresh();
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] auditManager.Refresh() returned ({sw.ElapsedMilliseconds}ms)");

        var prefixMap = auditManager.GetPrefixToAuditMap();
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Audit map count: {prefixMap.Count}");

        bool loadedFromAudit = false;
        if (prefixMap.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: Using audit JSON files");
            // Use the first audit source
            var auditPath = prefixMap.Values.First();
            System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Audit path: {auditPath}");
            var streamer = new LogStreamer(auditManager, auditPath);

            // Try to load history from audit JSON
            var history = streamer.LoadHistory();
            System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Audit JSON LoadHistory returned {history.Count} lines");

            // If audit JSON has current files with content, use it
            if (history.Count > 0)
            {
                LoadFromLines(directoryPath, history);
                Title = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                StartStreaming(streamer);

                // Set up rotation watching
                var watcher = new LogWatcher(auditManager);
                var strategy = watcher.CreateStrategy(AppConstants.RotationStrategyAuditJson);
                watcher.UseStrategy(strategy);
                watcher.ActiveFileChanged += (_, e) => streamer.PivotToNewFile(e.NewFile);
                watcher.Start();
                _watcher = watcher;
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: Audit loading complete with {history.Count} lines");
                loadedFromAudit = true;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] WARNING: Audit JSON returned 0 lines (files may be rotated out)");
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Falling back to pattern/file discovery");
            }
        }
        // If we didn't load from audit JSON (either no audit files or they returned 0 lines), fall back to pattern/file discovery
        if (!loadedFromAudit)
        {
            System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: Trying pattern discovery as fallback");
            // Fallback: discover by filename pattern
            var groups = AuditLogManager.DiscoverByPattern(directoryPath);
            System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Pattern discovery groups: {groups.Count}");

            if (groups.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: Using pattern-discovered files");
                var files = groups.Values.First();
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Discovered {files.Count} files");
                var streamer = new LogStreamer(files);
                LoadHistoryAndStream(streamer, directoryPath);
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: Pattern loading complete");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: No patterns, trying .log and .txt files");
                // Last resort: find any .log or .txt file
                var logFiles = Directory.GetFiles(directoryPath, "*.log").OrderBy(f => f).ToList();
                System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Found {logFiles.Count} .log files");

                if (logFiles.Count == 0)
                {
                    logFiles = Directory.GetFiles(directoryPath, "*.txt").OrderBy(f => f).ToList();
                    System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] Found {logFiles.Count} .txt files");
                }

                if (logFiles.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: Loading {logFiles.Count} log files");
                    var streamer = new LogStreamer(logFiles);
                    LoadHistoryAndStream(streamer, directoryPath);
                    System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] BRANCH: File loading complete");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] ERROR: No logs found in directory!");
                    Title = $"Empty: {Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] ====== Completed in {sw.ElapsedMilliseconds}ms ======");
    }

    private void LoadHistoryAndStream(LogStreamer streamer, string directoryPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] LoadHistoryAndStream: calling streamer.LoadHistory() ({sw.ElapsedMilliseconds}ms)");
        var history = streamer.LoadHistory();
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] LoadHistoryAndStream: LoadHistory returned {history.Count} lines ({sw.ElapsedMilliseconds}ms)");
        LoadFromLines(directoryPath, history);
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] LoadHistoryAndStream: LoadFromLines completed ({sw.ElapsedMilliseconds}ms)");
        Title = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        StartStreaming(streamer);
        System.Diagnostics.Debug.WriteLine($"[LOAD FOLDER] LoadHistoryAndStream: completed ({sw.ElapsedMilliseconds}ms)");
    }

    private void StartStreaming(LogStreamer streamer)
    {
        _streamer?.Dispose();
        _streamer = streamer;

        streamer.LinesReceived += OnStreamerLinesReceived;
        streamer.StartTailing();
        IsStreaming = true;
    }

    private void OnStreamerLinesReceived(IReadOnlyList<string> rawLines)
    {
        AvDispatcher.UIThread.Post(() =>
        {
            if (_memorySource is null) return;

            var baseIndex = _memorySource.Count;
            var parsed = rawLines.Select((raw, i) => LogLineParser.Parse(raw, baseIndex + i)).ToList();
            _memorySource.AppendLines(parsed);
            TotalLineCount = _memorySource.Count;

            // Update end date from last appended line
            for (int i = parsed.Count - 1; i >= 0; i--)
            {
                if (parsed[i].Timestamp is { } ts)
                {
                    _dateEnd = ts;
                    RaiseHeaderMetaChanged();
                    break;
                }
            }

            // Update grid source incrementally
            if (IsGridMode && _gridRootRows is { Count: > 0 })
            {
                var lastGroup = _gridRootRows[^1];
                if (lastGroup.Children is not null)
                {
                    foreach (var line in parsed)
                    {
                        if (!line.IsFileSeparator)
                            lastGroup.Children.Add(new GridRowViewModel { Line = new LogLineViewModel(line) });
                    }
                    lastGroup.ChildCount = lastGroup.Children.Count;
                }
            }

            if (IsFollowMode)
                RequestScrollToEndThrottled();
        });
    }

    [RelayCommand]
    private void ToggleFollow()
    {
        IsFollowMode = !IsFollowMode;
        if (IsFollowMode)
            RequestScrollToEndThrottled();
    }

    private void DisposeProvider()
    {
        if (_streamer is not null)
            _streamer.LinesReceived -= OnStreamerLinesReceived;
        _streamer?.Dispose();
        _streamer = null;
        _watcher?.Dispose();
        _watcher = null;

        // Unsubscribe provider events before disposing
        if (_provider is not null)
        {
            if (_providerLinesAppended is not null)
                _provider.LinesAppended -= _providerLinesAppended;
            if (_providerIndexingCompleted is not null)
                _provider.IndexingCompleted -= _providerIndexingCompleted;
            if (_providerIndexingProgressChanged is not null)
                _provider.IndexingProgressChanged -= _providerIndexingProgressChanged;
        }
        _providerLinesAppended = null;
        _providerIndexingCompleted = null;
        _providerIndexingProgressChanged = null;

        _provider?.Dispose();
        _provider = null;
        _virtualSource?.Dispose();
        _virtualSource = null;
        _memorySource = null;
        _scrollPending = false;
        IsStreaming = false;
        _fileCount = 0;
        _sourceCount = 0;
        _dateStart = null;
        _dateEnd = null;
        ResetLineSelection();
    }

    /// <summary>Helper to find and track source ID by physical path.</summary>
    private void TrackSourceByPath(string path)
    {
        if (_sourceManager == null) return;

        var source = _sourceManager.Sources.FirstOrDefault(s =>
            s.PhysicalPath.Equals(path, StringComparison.OrdinalIgnoreCase) && !s.IsChild);

        if (source != null)
            _loadedSourceIds.Add(source.SourceId);
    }

    public void Dispose()
    {
        DisposeProvider();
        Filter.Dispose();
    }

    /// <summary>Clear contents if the removed source is loaded in this pane.</summary>
    public void ClearIfSourceRemoved(SourceItemViewModel removedSource)
    {
        bool shouldClear = false;

        // Check by source ID
        if (_loadedSourceIds.Contains(removedSource.SourceId))
            shouldClear = true;

        // Check by physical path (handles cases where source wasn't in SourceManager yet)
        if (!shouldClear && !string.IsNullOrEmpty(removedSource.PhysicalPath))
        {
            var removedPath = Path.GetFullPath(removedSource.PhysicalPath);
            if (_loadedPaths.Contains(removedPath))
                shouldClear = true;
        }

        if (shouldClear)
        {
            DisposeProvider();
            _loadedSourceIds.Clear();
            _loadedPaths.Clear();
            _delegatingSource.SetInner(PlaceholderLine);
            Title = "No file loaded";
            TotalLineCount = 1;

            // Clear filter subscriptions
            Filter.OnSourceChanged(null);
        }
    }
}

public class SourceKindToIconConverter : IValueConverter
{
    public static readonly SourceKindToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SourceKind kind)
        {
            return kind switch
            {
                SourceKind.Folder => "\uE8B7",
                SourceKind.File => "\uE7C3",
                SourceKind.Merge => "\uE8D5",
                _ => "\uE7C3"
            };
        }
        return "\uE7C3";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
