using System.Collections;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Avalonia.Controls;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using AvDispatcher = global::Avalonia.Threading.Dispatcher;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the bottom-docked search/filter panel.
/// Debounced 500ms search across in-memory or BigFile lines.
/// </summary>
public partial class FilterPanelViewModel : ObservableObject, IDisposable
{
    private const int DebounceMs = 500;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _searchMode = "Regex";
    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isFollowMode;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private IEnumerable? _resultItems;
    [ObservableProperty] private int _resultCount;

    private readonly System.Timers.Timer _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private LogViewViewModel? _boundLogView;
    private CompiledMatcher? _currentMatcher;
    private int _lastProcessedIndex;

    /// <summary>Fired when search hits change. Carries sorted list of matching line indices.</summary>
    public event Action<List<long>>? SearchHitsChanged;

    public string[] AvailableModes { get; } = ["Regex", "Literal", "Wildcard"];

    public FilterPanelViewModel()
    {
        _debounceTimer = new System.Timers.Timer(DebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => RunSearch();
    }

    /// <summary>Bind this filter to a log view's data for searching.</summary>
    public void BindTo(LogViewViewModel logView)
    {
        _boundLogView = logView;
        if (!string.IsNullOrEmpty(SearchText))
            RestartDebounce();
    }

    /// <summary>Called by LogViewViewModel when source changes to resubscribe to events.</summary>
    internal void OnSourceChanged(InMemoryLogItemsSource? newSource)
    {
        // Unsubscribe from old source
        if (_boundLogView?.MemorySource is { } oldSource && oldSource != newSource)
            oldSource.CollectionChanged -= OnSourceCollectionChanged;

        // Subscribe to new source
        if (newSource is not null)
            newSource.CollectionChanged += OnSourceCollectionChanged;

        // Reset tracking when source changes
        _lastProcessedIndex = 0;
    }

    private void OnSourceCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // If we have an active search, check new lines
        if (_currentMatcher is not null && _boundLogView?.MemorySource is { } memSource)
        {
            ProcessIncrementalUpdate(memSource);
        }
    }

    private void ProcessIncrementalUpdate(InMemoryLogItemsSource memSource)
    {
        if (_currentMatcher is null || ResultItems is not InMemoryLogItemsSource resultSource)
            return;

        var newMatches = new List<LogLine>();
        var newHits = new List<long>();

        // Process only new lines since last update
        for (int i = _lastProcessedIndex; i < memSource.Count; i++)
        {
            var vm = memSource[i];
            if (_currentMatcher.IsMatch(vm.RawText))
            {
                newHits.Add(vm.GlobalIndex);
                newMatches.Add(LogLineParser.Parse(vm.RawText, vm.GlobalIndex));
            }
        }

        _lastProcessedIndex = memSource.Count;

        if (newMatches.Count > 0)
        {
            AvDispatcher.UIThread.Post(() =>
            {
                resultSource.AppendLines(newMatches);
                ResultCount += newMatches.Count;
                StatusText = $"{ResultCount:N0} matches";

                // Notify about all hits (not just new ones) for navigation
                var allHits = new List<long>();
                for (int i = 0; i < resultSource.Count; i++)
                    allHits.Add(resultSource[i].GlobalIndex);
                SearchHitsChanged?.Invoke(allHits);
            });
        }
    }

    partial void OnSearchTextChanged(string value) => RestartDebounce();
    partial void OnSearchModeChanged(string value) => RestartDebounce();
    partial void OnCaseSensitiveChanged(bool value) => RestartDebounce();

    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void RunSearch()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(SearchText) || _boundLogView is null)
        {
            ClearResults();
            _currentMatcher = null;
            if (_boundLogView is not null)
            {
                _boundLogView.ActiveSearchMatcher = null;
                AvDispatcher.UIThread.Post(() => _boundLogView.NotifyRowVisualsChanged());
            }
            _lastProcessedIndex = 0;
            return;
        }

        var mode = SearchMode switch
        {
            "Literal" => Core.Services.SearchMode.Literal,
            "Wildcard" => Core.Services.SearchMode.Wildcard,
            _ => Core.Services.SearchMode.Regex
        };

        var matcher = SearchEngine.TryCompile(SearchText, mode, CaseSensitive);
        if (matcher is null)
        {
            AvDispatcher.UIThread.Post(() => StatusText = "Invalid pattern");
            _currentMatcher = null;
            _lastProcessedIndex = 0;
            return;
        }

        _currentMatcher = matcher;
        if (_boundLogView is not null)
        {
            _boundLogView.ActiveSearchMatcher = matcher;
            AvDispatcher.UIThread.Post(() => _boundLogView.NotifyRowVisualsChanged());
        }
        _lastProcessedIndex = 0;
        Task.Run(() => ExecuteSearch(matcher, ct), ct);
    }

    private void ExecuteSearch(CompiledMatcher matcher, CancellationToken ct)
    {
        var source = _boundLogView;
        if (source is null) return;

        var hits = new List<long>();
        var matchedLines = new List<LogLine>();

        // Access the actual source, not the delegating wrapper
        if (source.MemorySource is { } memSource)
        {
            for (int i = 0; i < memSource.Count && !ct.IsCancellationRequested; i++)
            {
                var vm = memSource[i];
                if (matcher.IsMatch(vm.RawText))
                {
                    hits.Add(vm.GlobalIndex);
                    matchedLines.Add(LogLineParser.Parse(vm.RawText, vm.GlobalIndex));
                }
            }
        }
        else if (source.VirtualSource is { } virtSource)
        {
            long total = virtSource.Count;
            for (long i = 0; i < total && !ct.IsCancellationRequested; i++)
            {
                var vm = virtSource[(int)i];
                if (matcher.IsMatch(vm.RawText))
                {
                    hits.Add(vm.GlobalIndex);
                    matchedLines.Add(LogLineParser.Parse(vm.RawText, vm.GlobalIndex));
                }
            }
        }

        if (ct.IsCancellationRequested) return;

        // Track how many lines we've processed
        int processedCount = 0;
        if (source.MemorySource is { } mem)
            processedCount = mem.Count;
        else if (source.VirtualSource is { } virt)
            processedCount = virt.Count;

        AvDispatcher.UIThread.Post(() =>
        {
            var resultSource = new InMemoryLogItemsSource();
            resultSource.AddRange(matchedLines);
            ResultItems = resultSource;
            ResultCount = hits.Count;
            StatusText = $"{hits.Count:N0} matches";
            SearchHitsChanged?.Invoke(hits);
            _lastProcessedIndex = processedCount;
        });
    }

    private void ClearResults()
    {
        AvDispatcher.UIThread.Post(() =>
        {
            ResultItems = null;
            ResultCount = 0;
            StatusText = "";
            SearchHitsChanged?.Invoke([]);
        });
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
        ClearResults();
        if (_boundLogView is not null)
        {
            _boundLogView.ActiveSearchMatcher = null;
            _boundLogView.NotifyRowVisualsChanged();
        }
        SearchText = "";
    }

    [RelayCommand]
    private void ToggleCaseSensitive() => CaseSensitive = !CaseSensitive;

    public void Show()
    {
        IsVisible = true;
    }

    public void ActivateResult(LogLineViewModel line)
    {
        _boundLogView?.NavigateToLine(line.GlobalIndex);
    }

    public void Dispose()
    {
        _debounceTimer.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
