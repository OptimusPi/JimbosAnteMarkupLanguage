using System.ComponentModel;
using Motely.Enums;
using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// These small public views are how code reads a logic clause or a booster's shape back after the
/// loader has built it. They are cheap to exercise and easy to break silently.
/// </summary>
public sealed class LogicAndBoosterCoverageTests
{
    [Fact]
    public void AndClause_PayloadRoundTripsEveryParameter()
    {
        var child = new MotelyClause();
        var clause = new AndClause
        {
            Mode = LogicScoreMode.Max,
            Clauses = [child],
            Label = "party",
            Min = 2,
            Max = 3,
            Score = 7,
        };

        var payload = clause.And;
        Assert.Equal(LogicScoreMode.Max, payload.Mode);
        Assert.Equal([child], payload.Clauses);
        Assert.Equal("party", payload.Label);
        Assert.Equal(2, payload.Min);
        Assert.Equal(3, payload.Max);
        Assert.Equal(7, payload.Score);

        var restored = new AndClause
        {
            And = new AndPayload
            {
                Mode = LogicScoreMode.Sum,
                Clauses = [child],
                Label = "again",
                Min = 1,
                Max = null,
                Score = 4,
            },
        };
        Assert.Equal(LogicScoreMode.Sum, restored.Mode);
        Assert.Equal([child], restored.Clauses);
        Assert.Equal("again", restored.Label);
        Assert.Equal(1, restored.Min);
        Assert.Null(restored.Max);
        Assert.Equal(4, restored.Score);
    }

    [Fact]
    public void OrClause_PayloadRoundTripsEveryParameter()
    {
        var child = new MotelyClause();
        var clause = new OrClause
        {
            Mode = LogicScoreMode.Max,
            Clauses = [child],
            Label = "find",
            Min = 2,
            Max = 3,
            Score = 7,
        };

        var payload = clause.Or;
        Assert.Equal(LogicScoreMode.Max, payload.Mode);
        Assert.Equal([child], payload.Clauses);
        Assert.Equal("find", payload.Label);
        Assert.Equal(2, payload.Min);
        Assert.Equal(3, payload.Max);
        Assert.Equal(7, payload.Score);

        var restored = new OrClause
        {
            Or = new OrPayload
            {
                Mode = LogicScoreMode.Sum,
                Clauses = [child],
                Label = "play",
                Min = 1,
                Max = null,
                Score = 4,
            },
        };
        Assert.Equal(LogicScoreMode.Sum, restored.Mode);
        Assert.Equal([child], restored.Clauses);
        Assert.Equal("play", restored.Label);
        Assert.Equal(1, restored.Min);
        Assert.Null(restored.Max);
        Assert.Equal(4, restored.Score);
    }

    [Fact]
    public void BoosterPack_CardAndChoiceCountsFollowThePackShape()
    {
        Assert.Equal(2, MotelyBoosterPack.Spectral.GetPackCardCount());
        Assert.Equal(4, MotelyBoosterPack.JumboBuffoon.GetPackCardCount());
        Assert.Equal(3, MotelyBoosterPack.Standard.GetPackCardCount());
        Assert.Equal(5, MotelyBoosterPack.MegaCelestial.GetPackCardCount());

        Assert.Equal(1, MotelyBoosterPack.JumboArcana.GetPackChoiceCount());
        Assert.Equal(2, MotelyBoosterPack.MegaSpectral.GetPackChoiceCount());

        var invalidSize = (MotelyBoosterPackSize)3;
        Assert.Throws<InvalidEnumArgumentException>(
            () => MotelyBoosterPackType.Arcana.GetCardCount(invalidSize)
        );
        Assert.Throws<InvalidEnumArgumentException>(
            () => MotelyBoosterPackType.Spectral.GetCardCount(invalidSize)
        );
        Assert.Throws<InvalidEnumArgumentException>(
            () => ((MotelyBoosterPackType)99).GetCardCount(MotelyBoosterPackSize.Normal)
        );
        Assert.Throws<InvalidEnumArgumentException>(
            () => ((MotelyBoosterPack)((int)MotelyBoosterPackType.Arcana << MotelyGlobals.BoosterPackTypeOffset | 3))
                .GetPackChoiceCount()
        );
    }
}
