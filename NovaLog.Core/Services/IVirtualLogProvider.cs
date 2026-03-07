using NovaLog.Core.Models;

namespace NovaLog.Core.Services;

/// <summary>
/// Provides on-demand, random-access line retrieval for files too large to load into memory.
/// </summary>
public interface IVirtualLogProvider : IDisposable
{
    long LineCount { get; }
    bool IsIndexing { get; }
    double IndexingProgress { get; }
    string FilePath { get; }

    LogLine? GetLine(long lineIndex);
    IReadOnlyList<LogLine> GetPage(long startLine, int count);
    string? GetRawLine(long lineIndex);
    void ScrollToTimestamp(DateTime target, Action<long> onFound);

    event Action<long>? IndexingProgressChanged;
    event Action? IndexingCompleted;
    event Action<long>? LinesAppended;
}
