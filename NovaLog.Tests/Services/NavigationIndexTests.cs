using NovaLog.Core.Models;
using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class NavigationIndexTests
{
    // ── GetNext / BinarySearch ───────────────────────────────────────

    [Fact]
    public void GetNext_Forward_ReturnsNextItem()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30, 40, 50]);

        Assert.Equal(30, nav.GetNext(NavigationCategory.Error, 25, forward: true));
    }

    [Fact]
    public void GetNext_Forward_WrapsAround()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30]);

        Assert.Equal(10, nav.GetNext(NavigationCategory.Error, 30, forward: true));
    }

    [Fact]
    public void GetNext_Backward_ReturnsPreviousItem()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30, 40, 50]);

        Assert.Equal(20, nav.GetNext(NavigationCategory.Error, 25, forward: false));
    }

    [Fact]
    public void GetNext_Backward_WrapsAround()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30]);

        Assert.Equal(30, nav.GetNext(NavigationCategory.Error, 5, forward: false));
    }

    [Fact]
    public void GetNext_ExactMatch_SkipsToNext()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30]);

        Assert.Equal(30, nav.GetNext(NavigationCategory.Error, 20, forward: true));
        Assert.Equal(10, nav.GetNext(NavigationCategory.Error, 20, forward: false));
    }

    [Fact]
    public void GetNext_EmptyList_ReturnsNegative()
    {
        var nav = new NavigationIndex();

        Assert.Equal(-1, nav.GetNext(NavigationCategory.Error, 10, forward: true));
        Assert.Equal(-1, nav.GetNext(NavigationCategory.Error, 10, forward: false));
    }

    [Fact]
    public void GetNext_SingleItem_AlwaysReturnsThat()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([42]);

        Assert.Equal(42, nav.GetNext(NavigationCategory.Error, 0, forward: true));
        Assert.Equal(42, nav.GetNext(NavigationCategory.Error, 100, forward: false));
    }

    // ── ToggleBookmark ──────────────────────────────────────────────

    [Fact]
    public void ToggleBookmark_Add_ReturnsTrue()
    {
        var nav = new NavigationIndex();

        Assert.True(nav.ToggleBookmark(42));
        Assert.True(nav.IsBookmarked(42));
        Assert.Equal(1, nav.BookmarkCount);
    }

    [Fact]
    public void ToggleBookmark_Remove_ReturnsFalse()
    {
        var nav = new NavigationIndex();
        nav.ToggleBookmark(42);
        Assert.False(nav.ToggleBookmark(42));
        Assert.False(nav.IsBookmarked(42));
        Assert.Equal(0, nav.BookmarkCount);
    }

    [Fact]
    public void ToggleBookmark_MaintainsSortOrder()
    {
        var nav = new NavigationIndex();
        nav.ToggleBookmark(30);
        nav.ToggleBookmark(10);
        nav.ToggleBookmark(20);

        var bookmarks = nav.Bookmarks;
        Assert.Equal(3, bookmarks.Count);
        Assert.Equal(10, bookmarks[0]);
        Assert.Equal(20, bookmarks[1]);
        Assert.Equal(30, bookmarks[2]);
    }

    // ── GetPositionInfo ─────────────────────────────────────────────

    [Fact]
    public void GetPositionInfo_ExactMatch_Returns1Based()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30, 40]);

        var (idx, total) = nav.GetPositionInfo(NavigationCategory.Error, 20);
        Assert.Equal(2, idx);
        Assert.Equal(4, total);
    }

    [Fact]
    public void GetPositionInfo_NotExactMatch_ReturnsNearestPrevious()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30]);

        var (idx, total) = nav.GetPositionInfo(NavigationCategory.Error, 25);
        Assert.Equal(2, idx);
        Assert.Equal(3, total);
    }

    [Fact]
    public void GetPositionInfo_BeforeFirst_ReturnsZero()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20, 30]);

        var (idx, total) = nav.GetPositionInfo(NavigationCategory.Error, 5);
        Assert.Equal(0, idx);
        Assert.Equal(3, total);
    }

    [Fact]
    public void GetPositionInfo_Empty_ReturnsZeros()
    {
        var nav = new NavigationIndex();
        var (idx, total) = nav.GetPositionInfo(NavigationCategory.Error, 10);
        Assert.Equal(0, idx);
        Assert.Equal(0, total);
    }

    // ── ComputeDensityBuckets ───────────────────────────────────────

    [Fact]
    public void ComputeDensityBuckets_DistributesCorrectly()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([5, 15, 25, 35, 45]);

        var buckets = nav.ComputeDensityBuckets(NavigationCategory.Error, 50, 5);

        Assert.Equal(5, buckets.Length);
        Assert.Equal(1, buckets[0]);
        Assert.Equal(1, buckets[1]);
        Assert.Equal(1, buckets[2]);
        Assert.Equal(1, buckets[3]);
        Assert.Equal(1, buckets[4]);
    }

    [Fact]
    public void ComputeDensityBuckets_EmptyList_ReturnsZeros()
    {
        var nav = new NavigationIndex();
        var buckets = nav.ComputeDensityBuckets(NavigationCategory.Error, 100, 10);

        Assert.Equal(10, buckets.Length);
        Assert.All(buckets, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ComputeDensityBuckets_ZeroTotalLines_ReturnsZeros()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([1, 2, 3]);

        var buckets = nav.ComputeDensityBuckets(NavigationCategory.Error, 0, 5);
        Assert.Equal(5, buckets.Length);
        Assert.All(buckets, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ComputeDensityBuckets_Clustering()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);

        var buckets = nav.ComputeDensityBuckets(NavigationCategory.Error, 100, 10);

        Assert.Equal(10, buckets[0]);
        for (int i = 1; i < 10; i++)
            Assert.Equal(0, buckets[i]);
    }

    // ── ScanLevels (in-memory) ──────────────────────────────────────

    [Fact]
    public void ScanLevels_InMemory_FindsErrorsAndWarns()
    {
        var lines = new List<LogLine>
        {
            new() { GlobalIndex = 0, Level = LogLevel.Info },
            new() { GlobalIndex = 1, Level = LogLevel.Error },
            new() { GlobalIndex = 2, Level = LogLevel.Warn },
            new() { GlobalIndex = 3, Level = LogLevel.Debug },
            new() { GlobalIndex = 4, Level = LogLevel.Fatal },
            new() { GlobalIndex = 5, Level = LogLevel.Warn },
        };

        var (errors, warns) = NavigationIndex.ScanLevels(lines);

        Assert.Equal(2, errors.Count);
        Assert.Contains(1L, errors);
        Assert.Contains(4L, errors);

        Assert.Equal(2, warns.Count);
        Assert.Contains(2L, warns);
        Assert.Contains(5L, warns);
    }

    [Fact]
    public void ScanLevels_EmptyList_ReturnsEmpty()
    {
        var (errors, warns) = NavigationIndex.ScanLevels(new List<LogLine>());
        Assert.Empty(errors);
        Assert.Empty(warns);
    }

    // ── AppendLevelResults ──────────────────────────────────────────

    [Fact]
    public void AppendLevelResults_AccumulatesIncrementally()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([10, 20]);
        nav.AppendLevelResults([30, 40], [100]);

        Assert.Equal(4, nav.ErrorCount);
        Assert.Equal(1, nav.WarnCount);
    }

    // ── IndicesChanged event ────────────────────────────────────────

    [Fact]
    public void IndicesChanged_FiresOnMutation()
    {
        var nav = new NavigationIndex();
        int fired = 0;
        nav.IndicesChanged += () => fired++;

        nav.SetErrors([1, 2, 3]);
        nav.SetWarns([4, 5]);
        nav.SetSearchHits([6]);
        nav.ToggleBookmark(7);

        Assert.Equal(4, fired);
    }

    // ── Thread safety ───────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAccess_DoesNotThrow()
    {
        var nav = new NavigationIndex();
        nav.SetErrors(Enumerable.Range(0, 1000).Select(i => (long)i).ToList());

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                nav.GetNext(NavigationCategory.Error, i * 10, forward: true);
                nav.GetPositionInfo(NavigationCategory.Error, i * 10);
                nav.ComputeDensityBuckets(NavigationCategory.Error, 1000, 50);
                nav.IsBookmarked(i);
            }
        }));

        await Task.WhenAll(tasks);
    }

    // ── Category routing ────────────────────────────────────────────

    [Fact]
    public void DifferentCategories_AreIndependent()
    {
        var nav = new NavigationIndex();
        nav.SetErrors([100]);
        nav.SetWarns([200]);
        nav.SetSearchHits([300]);
        nav.ToggleBookmark(400);

        Assert.Equal(100, nav.GetNext(NavigationCategory.Error, 0, true));
        Assert.Equal(200, nav.GetNext(NavigationCategory.Warn, 0, true));
        Assert.Equal(300, nav.GetNext(NavigationCategory.SearchHit, 0, true));
        Assert.Equal(400, nav.GetNext(NavigationCategory.Bookmark, 0, true));
    }
}
