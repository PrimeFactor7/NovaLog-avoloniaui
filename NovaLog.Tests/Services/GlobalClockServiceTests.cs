using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class GlobalClockServiceTests
{
    [Fact]
    public async Task BroadcastTime_FiresAfterDebounce()
    {
        using var service = new GlobalClockService();
        var sender = new object();
        DateTime? receivedTime = null;
        object? receivedSender = null;

        service.TimeChanged += (ts, s) =>
        {
            receivedTime = ts;
            receivedSender = s;
        };

        var testTime = new DateTime(2025, 1, 15, 10, 30, 0);
        service.BroadcastTime(testTime, sender);

        // Wait for the 100ms debounce + margin
        await Task.Delay(250);

        Assert.Equal(testTime, receivedTime);
        Assert.Same(sender, receivedSender);
    }

    [Fact]
    public async Task MultipleRapidCalls_OnlyFiresOnce()
    {
        using var service = new GlobalClockService();
        var sender = new object();
        int fireCount = 0;
        DateTime? lastTime = null;

        service.TimeChanged += (ts, _) =>
        {
            Interlocked.Increment(ref fireCount);
            lastTime = ts;
        };

        // Rapid fire 5 times within debounce window
        for (int i = 0; i < 5; i++)
            service.BroadcastTime(new DateTime(2025, 1, 15, 10, 30, i), sender);

        await Task.Delay(250);

        Assert.Equal(1, fireCount);
        Assert.Equal(new DateTime(2025, 1, 15, 10, 30, 4), lastTime);
    }

    [Fact]
    public async Task SenderPassedThroughCorrectly()
    {
        using var service = new GlobalClockService();
        var senderA = new object();
        var senderB = new object();
        object? receivedSender = null;

        service.TimeChanged += (_, s) => receivedSender = s;

        service.BroadcastTime(DateTime.Now, senderA);
        await Task.Delay(250);

        Assert.Same(senderA, receivedSender);

        // Now broadcast from sender B
        receivedSender = null;
        service.BroadcastTime(DateTime.Now, senderB);
        await Task.Delay(250);

        Assert.Same(senderB, receivedSender);
    }

    [Fact]
    public async Task Dispose_StopsTimer()
    {
        var service = new GlobalClockService();
        var sender = new object();
        int fireCount = 0;

        service.TimeChanged += (_, _) => Interlocked.Increment(ref fireCount);

        service.BroadcastTime(DateTime.Now, sender);
        service.Dispose();

        await Task.Delay(250);

        Assert.Equal(0, fireCount);
    }
}
