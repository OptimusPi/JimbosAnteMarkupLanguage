namespace Motely.Tests;

public class TopSeedSinkCollectorTests
{
    [Fact]
    public void EmptyCollectorReturnsEmpty()
    {
        var c = new MotelyTopSeedSink.Collector(3);
        Assert.Empty(c.GetSeeds());
    }

    [Fact]
    public void UnboundedCollectorKeepsAll()
    {
        var c = new MotelyTopSeedSink.Collector(int.MaxValue);
        c.Consider("A", 1);
        c.Consider("B", 2);
        c.Consider("C", 3);
        Assert.Equal(["C", "B", "A"], c.GetSeeds());
    }

    [Fact]
    public void BoundedKeepsTopNByScore()
    {
        var c = new MotelyTopSeedSink.Collector(2);
        c.Consider("low", 1);
        c.Consider("mid", 5);
        c.Consider("high", 10);
        c.Consider("mid2", 5);
        Assert.Equal(["high", "mid"], c.GetSeeds());
    }

    [Fact]
    public void TiesBreakByInsertionOrder()
    {
        var c = new MotelyTopSeedSink.Collector(2);
        c.Consider("first", 5);
        c.Consider("second", 5);
        c.Consider("third", 5);
        // earlier wins on tie; only 2 survive
        Assert.Equal(["first", "second"], c.GetSeeds());
    }

    [Fact]
    public void GetSeedsDedupesSeeds()
    {
        var c = new MotelyTopSeedSink.Collector(int.MaxValue);
        c.Consider("SAME", 1);
        c.Consider("SAME", 9);
        // distinct by seed string
        Assert.Single(c.GetSeeds());
    }

    [Fact]
    public void LimitZeroKeepsNothing()
    {
        var c = new MotelyTopSeedSink.Collector(0);
        c.Consider("A", 100);
        Assert.Empty(c.GetSeeds());
    }
}