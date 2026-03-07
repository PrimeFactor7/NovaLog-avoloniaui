namespace NovaLog.Core.Services;

/// <summary>
/// Extended provider for chronologically merged multi-source views.
/// Color-agnostic: returns hex string instead of System.Drawing.Color.
/// </summary>
public interface IMergedLogProvider : IVirtualLogProvider
{
    (string Tag, string TagColorHex) GetSourceInfo(long mergedLineIndex);
    int MaxTagLength { get; }
}
