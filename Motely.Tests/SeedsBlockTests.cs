namespace Motely.Tests;

/// <summary>MotelyTopSeedSink.RewriteSeedsBlock: the parser finds the block, the rest stays as written.</summary>
public class SeedsBlockTests
{
    private static string Rewrite(string text, params string[] seeds) =>
        MotelyTopSeedSink.RewriteSeedsBlock(text, seeds);

    [Fact]
    public void AppendsTheBlockWhenThereIsNone()
    {
        Assert.Equal("name: X\nmust: []\n\nseeds:\n  - NEW1\n", Rewrite("name: X\nmust: []\n", "NEW1"));
    }

    [Fact]
    public void KeepsListedSeedsFirstAndDropsRepeats()
    {
        Assert.Equal(
            "name: X\nseeds:\n  - OLD1\n  - AAA\n  - NEW1\nmust: []\n",
            Rewrite("name: X\nseeds:\n  - OLD1\n  - AAA\nmust: []\n", "NEW1", "OLD1")
        );
    }

    [Theory]
    [InlineData("seeds: [AAA, BBB]\nmust: []\n")]
    [InlineData("seeds: [AAA,\n  BBB]\nmust: []\n")]
    public void FlowListBecomesABlock(string text)
    {
        Assert.Equal("seeds:\n  - AAA\n  - BBB\n  - NEW1\nmust: []\n", Rewrite(text, "NEW1"));
    }

    [Fact]
    public void KeepsWhatSurroundsTheBlock()
    {
        Assert.Equal(
            "description: \"a # b: seeds: c\" # note\nseeds:\n  - AAA\n  - NEW1\n\nmust: [] # end\n",
            Rewrite("description: \"a # b: seeds: c\" # note\nseeds: [AAA]\n\nmust: [] # end\n", "NEW1")
        );
    }

    [Fact]
    public void KeepsCrLf()
    {
        Assert.Equal(
            "name: X\r\nseeds:\r\n  - AAA\r\n  - NEW1\r\nmust: []\r\n",
            Rewrite("name: X\r\nseeds:\r\n  - AAA\r\nmust: []\r\n", "NEW1")
        );
    }

    [Fact]
    public void QuotedKeyIsTheBlockNotASecondOne()
    {
        Assert.Equal("seeds:\n  - AAA\n  - NEW1\nmust: []\n", Rewrite("\"seeds\": [AAA]\nmust: []\n", "NEW1"));
    }

    [Fact]
    public void NestedSeedsKeyIsNotTheBlock()
    {
        Assert.Equal(
            "must:\n  - joker: Blueprint\n    seeds: [ZZZ]\n\nseeds:\n  - NEW1\n",
            Rewrite("must:\n  - joker: Blueprint\n    seeds: [ZZZ]\n", "NEW1")
        );
    }

    [Fact]
    public void QuotedNullAndEmptySeedsStayQuoted()
    {
        Assert.Equal(
            "seeds:\n  - \"NULL\"\n  - \"\"\n  - NEW1\nmust: []\n",
            Rewrite("seeds:\n  - \"NULL\"\n  - \"\"\n  - ~\nmust: []\n", "NEW1")
        );
    }

    [Fact]
    public void BlockScalarSeedKeepsTheNextField()
    {
        Assert.Equal(
            "seeds:\n  - AAA\n  - NEW1\nmust: []\n",
            Rewrite("seeds:\n  - |-\n    AAA\nmust: []\n", "NEW1")
        );
    }

    [Fact]
    public void RewritingTwiceChangesNothing()
    {
        var once = Rewrite("name: X\nseeds:\n  - AAA\nmust: []\n", "NEW1");

        Assert.Equal(once, Rewrite(once, "NEW1"));
    }

    [Fact]
    public void RewrittenFilterStillLoads()
    {
        var text = Rewrite("deck: Red\nstake: White\nseeds: [AAA]\nmust:\n  - joker: Blueprint\n", "NEW1");
        var config = MotelyRunConfig.FromYaml(text);

        Assert.Equal(["AAA", "NEW1"], config.Seeds);
        Assert.IsType<JokerClause>(Assert.Single(config.Must));
    }
}
