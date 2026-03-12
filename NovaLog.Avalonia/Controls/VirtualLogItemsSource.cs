using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using NovaLog.Core.Models;
using NovaLog.Core.Services;
using NovaLog.Avalonia.ViewModels;
using AvDispatcher = global::Avalonia.Threading.Dispatcher;

namespace NovaLog.Avalonia.Controls;

public sealed class DelegatingItemsSource : IList, IReadOnlyList<LogLineViewModel>, INotifyCollectionChanged
{
    private static readonly LogLineViewModel[] Empty = [];
    private IReadOnlyList<LogLineViewModel> _inner = Empty;
    private INotifyCollectionChanged? _innerNotify;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public void SetInner(IReadOnlyList<LogLineViewModel> source)
    {
        if (ReferenceEquals(_inner, source)) return;

        if (_innerNotify is not null)
            _innerNotify.CollectionChanged -= OnInnerChanged;

        _inner = source;

        if (source is INotifyCollectionChanged ncc)
        {
            _innerNotify = ncc;
            ncc.CollectionChanged += OnInnerChanged;
        }
        else
        {
            _innerNotify = null;
        }

        CollectionChanged?.Invoke(this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnInnerChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => CollectionChanged?.Invoke(this, e);

    public int Count => _inner.Count;
    public LogLineViewModel this[int index] => _inner[index];
    public IEnumerator<LogLineViewModel> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IList
    public bool IsFixedSize => false;
    public bool IsReadOnly => true;
    object? IList.this[int index] { get => _inner[index]; set => throw new NotSupportedException(); }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => false;
    public int IndexOf(object? value) => -1;
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) { }
    public object SyncRoot => this;
    public bool IsSynchronized => false;
}

public sealed class VirtualLogItemsSource : IList, IReadOnlyList<LogLineViewModel>, INotifyCollectionChanged, IDisposable
{
    private readonly IVirtualLogProvider _provider;
    private readonly IMergedLogProvider? _mergedProvider;
    private readonly Dictionary<long, (LogLineViewModel Vm, LinkedListNode<long> Node)> _cache = new();
    private readonly LinkedList<long> _lruOrder = new();
    private readonly object _cacheLock = new();
    private const int CacheCapacity = 200;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public VirtualLogItemsSource(IVirtualLogProvider provider)
    {
        _provider = provider;
        _mergedProvider = provider as IMergedLogProvider;
        _provider.LinesAppended += OnLinesAppended;
        _provider.IndexingCompleted += OnIndexingCompleted;
    }

    public int Count => (int)Math.Min(_provider.LineCount, int.MaxValue);

    public LogLineViewModel this[int index]
    {
        get
        {
            long key = index;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var cached))
                {
                    _lruOrder.Remove(cached.Node);
                    _lruOrder.AddLast(cached.Node);
                    return cached.Vm;
                }
            }

            var line = _provider.GetLine(index);
            if (line is null)
                return new LogLineViewModel(new LogLine { GlobalIndex = index, Message = "" });

            // Propagate flavor to continuations
            if (line.Value.IsContinuation && line.Value.Flavor == SyntaxFlavor.None && index > 0)
            {
                var activeFlavor = GetActiveFlavor(index - 1);
                if (activeFlavor != SyntaxFlavor.None)
                {
                    line = line.Value with { Flavor = activeFlavor };
                }
            }

            var vm = CreateViewModel(line.Value, index);
            lock (_cacheLock) { AddToCache(key, vm); }
            return vm;
        }
    }

    private LogLineViewModel CreateViewModel(LogLine line, int index)
    {
        if (_mergedProvider is null)
            return new LogLineViewModel(line);

        var (tag, colorHex) = _mergedProvider.GetSourceInfo(index);
        return new LogLineViewModel(line, tag, colorHex);
    }

    /// <summary>
    /// Walk back to find the active syntax flavor for continuation line propagation.
    /// </summary>
    private SyntaxFlavor GetActiveFlavor(int fromIndex)
    {
        for (int i = fromIndex; i >= Math.Max(0, fromIndex - 50); i--)
        {
            var line = _provider.GetLine(i);
            if (line == null) continue;

            if (!line.Value.IsContinuation)
                return line.Value.Flavor;

            // Continuation with its own flavor (self-detected) becomes active
            if (line.Value.Flavor != SyntaxFlavor.None)
                return line.Value.Flavor;
        }
        return SyntaxFlavor.None;
    }

    private void AddToCache(long key, LogLineViewModel vm)
    {
        if (_cache.Count >= CacheCapacity && _lruOrder.First is { } oldest)
        {
            _cache.Remove(oldest.Value);
            _lruOrder.RemoveFirst();
        }
        var node = _lruOrder.AddLast(key);
        _cache[key] = (vm, node);
    }

    public void NotifyReset()
    {
        if (AvDispatcher.UIThread.CheckAccess())
        {
            lock (_cacheLock) { _cache.Clear(); _lruOrder.Clear(); }
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        else
        {
            AvDispatcher.UIThread.Post(NotifyReset);
        }
    }

    private void OnLinesAppended(long _) => NotifyReset();
    private void OnIndexingCompleted() => NotifyReset();

    public IEnumerator<LogLineViewModel> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IList
    public bool IsFixedSize => false;
    public bool IsReadOnly => true;
    object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => false;
    public int IndexOf(object? value) => -1;
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) { }
    public object SyncRoot => this;
    public bool IsSynchronized => false;

    public void Dispose()
    {
        _provider.LinesAppended -= OnLinesAppended;
        _provider.IndexingCompleted -= OnIndexingCompleted;
    }
}

public sealed class InMemoryLogItemsSource : IList, IReadOnlyList<LogLineViewModel>, INotifyCollectionChanged
{
    private readonly List<LogLineViewModel> _items = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _items.Count;

    public LogLineViewModel this[int index] => _items[index];

    public void AddRange(IEnumerable<LogLine> lines)
    {
        int startIdx = _items.Count;
        foreach (var line in lines)
            _items.Add(new LogLineViewModel(line));
        PropagateFlavorToContinuations(startIdx);
    }

    public void AppendLines(IEnumerable<LogLine> lines)
    {
        int startIdx = _items.Count;
        foreach (var line in lines)
            _items.Add(new LogLineViewModel(line));
        PropagateFlavorToContinuations(startIdx);
        CollectionChanged?.Invoke(this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Propagate syntax flavor to continuation lines that don't have their own flavor.
    /// </summary>
    private void PropagateFlavorToContinuations(int fromIndex)
    {
        // Determine the active flavor from the line just before the new batch
        var activeFlavor = SyntaxFlavor.None;
        if (fromIndex > 0 && fromIndex <= _items.Count)
        {
            // Walk back to find the most recent non-None flavor
            for (int i = fromIndex - 1; i >= 0; i--)
            {
                if (!_items[i].IsContinuation || _items[i].Flavor != SyntaxFlavor.None)
                {
                    activeFlavor = _items[i].Flavor;
                    break;
                }
            }
        }

        for (int i = fromIndex; i < _items.Count; i++)
        {
            var vm = _items[i];
            if (!vm.IsContinuation)
            {
                activeFlavor = vm.Flavor;
            }
            else
            {
                if (vm.Flavor == SyntaxFlavor.None && activeFlavor != SyntaxFlavor.None)
                    vm.Flavor = activeFlavor;
                if (vm.Flavor != SyntaxFlavor.None)
                    activeFlavor = vm.Flavor;
            }
        }
    }

    public void Clear()
    {
        _items.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<LogLineViewModel> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IList
    public bool IsFixedSize => false;
    public bool IsReadOnly => true;
    object? IList.this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    public int Add(object? value) => throw new NotSupportedException();
    public bool Contains(object? value) => false;
    public int IndexOf(object? value) => -1;
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) { }
    public object SyncRoot => this;
    public bool IsSynchronized => false;
}
