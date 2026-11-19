using Motely.Filters;
using Xunit;

namespace Motely.Tests;

// soulEditionRolls is exact when written: the edition prefilter reads that many Souls per ante,
// no more and no less. Left out, it reads one Soul per referenced pack slot.
public class SoulEditionRollsTests
{
    private static LegendaryJokerClause Load(string yaml)
    {
        Assert.True(JamlConfig.TryLoad(yaml, out var config, out var error), error);
        return Assert.IsType<LegendaryJokerClause>(Assert.Single(config!.Must));
    }

    private const string Perkeo = "must:\n  - legendaryJoker: Perkeo\n    edition: Negative\n    antes: [1]\n";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(50)]
    public void Written_IsExactlyTheSoulsRead(int rolls)
    {
        var clause = Load(Perkeo + $"    soulEditionRolls: {rolls}\n");
        Assert.Equal(rolls, LegendarySoulEditionPrefilter.GetEditionSoulRollCount(clause));
    }

    [Fact]
    public void One_PeeksAtFewerSoulsThanTheDefault()
    {
        int byDefault = LegendarySoulEditionPrefilter.GetEditionSoulRollCount(Load(Perkeo));
        Assert.True(byDefault > 1, $"default reads {byDefault} Souls");
        Assert.Equal(1, LegendarySoulEditionPrefilter.GetEditionSoulRollCount(Load(Perkeo + "    soulEditionRolls: 1\n")));
    }

    [Fact]
    public void LeftOut_ReadsOneSoulPerPackSlot()
    {
        var clause = Load(Perkeo + "    sources:\n      boosterPacks: [0, 1]\n");
        Assert.Equal(2, LegendarySoulEditionPrefilter.GetEditionSoulRollCount(clause));
    }
}
