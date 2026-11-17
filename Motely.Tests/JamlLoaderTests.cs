using YamlDotNet.Core;

namespace Motely.Tests;

/// <summary>MotelyRunConfig.FromYaml: the static (Vecc-generated) loader on real JAML shapes.</summary>
public class JamlLoaderTests
{
    private const string Head = "deck: Red\nstake: White\n";

    private static IMotelyClause OnlyMust(string must) =>
        Assert.Single(MotelyRunConfig.FromYaml(Head + "must:\n" + must).Must);

    [Fact]
    public void TopLevelFieldsLoad()
    {
        var config = MotelyRunConfig.FromYaml(
            "name: X\nauthor: Nat\ndescription: d\ndeck: Erratic\nstake: Gold\ncutoff: 3\nseeds: [ABC]\nmust: []\n"
        );

        Assert.Equal("X", config.Name);
        Assert.Equal("Nat", config.Author);
        Assert.Equal("d", config.Description);
        Assert.Equal(MotelyDeck.Erratic, config.Deck);
        Assert.Equal(MotelyStake.Gold, config.Stake);
        Assert.Equal(3, config.Cutoff);
        Assert.Equal(["ABC"], config.Seeds);
    }

    [Fact]
    public void ClauseKeyPicksTheClauseType()
    {
        var clause = Assert.IsType<RareJokerClause>(OnlyMust("  - rareJoker: Blueprint\n    antes: [1, 2]\n"));

        Assert.Equal([MotelyJokerRare.Blueprint], clause.Jokers);
        Assert.Equal([1, 2], clause.Antes);
    }

    [Fact]
    public void NameListLoadsEveryName()
    {
        var clause = Assert.IsType<JokerClause>(OnlyMust("  - jokers: [Blueprint, Brainstorm]\n"));

        Assert.Equal([MotelyJoker.Blueprint, MotelyJoker.Brainstorm], clause.Jokers);
    }

    [Theory]
    [InlineData("any")]
    [InlineData("Any")]
    [InlineData("~")]
    public void AnyWordPinsNoName(string any)
    {
        var clause = Assert.IsType<LegendaryJokerClause>(OnlyMust($"  - legendaryJoker: {any}\n"));

        Assert.Empty(clause.Jokers);
    }

    [Fact]
    public void OneAnteIsAListOfOne()
    {
        var clause = Assert.IsType<VoucherClause>(OnlyMust("  - voucher: Telescope\n    ante: 2\n"));

        Assert.Equal([MotelyVoucher.Telescope], clause.Vouchers);
        Assert.Equal([2], clause.Antes);
    }

    [Fact]
    public void OrAndAndNest()
    {
        var or = Assert.IsType<OrClause>(
            OnlyMust(
                "  - or:\n"
                    + "      - and:\n"
                    + "          - joker: OopsAll6s\n"
                    + "          - smallBlindTag: NegativeTag\n"
                    + "      - joker: Blueprint\n"
                    + "    score: 5\n"
            )
        );

        Assert.Equal(5, or.Score);
        Assert.Equal(2, or.Clauses.Length);
        var and = Assert.IsType<AndClause>(or.Clauses[0]);
        Assert.IsType<JokerClause>(and.Clauses[0]);
        Assert.IsType<SmallBlindTagClause>(and.Clauses[1]);
        Assert.IsType<JokerClause>(or.Clauses[1]);
    }

    [Fact]
    public void StandardCardScalarIsAWholeCard()
    {
        var clause = Assert.IsType<StandardCardClause>(OnlyMust("  - standardCard: KingOfHearts\n"));

        Assert.Equal(MotelyStandardcardRank.King, clause.Rank);
        Assert.Equal(MotelyStandardcardSuit.Hearts, clause.Suit);
    }

    [Fact]
    public void StandardCardAnyPinsNoCard()
    {
        var clause = Assert.IsType<StandardCardClause>(OnlyMust("  - standardCard: any\n"));

        Assert.Null(clause.Rank);
        Assert.Null(clause.Suit);
    }

    [Theory]
    [InlineData("  - jokr: Blueprint\n", 4, "Not a clause")]
    [InlineData("  - joker: Bluepritn\n", 4, "is not a joker name")]
    [InlineData("  - joker: Blueprint\n    antes: [1000]\n", 5, "out of range")]
    [InlineData("  - joker: Blueprint\n    antes: [1]\n    antes: [2]\n", 6, "duplicate key")]
    public void BadClauseNamesItsLine(string must, int line, string message)
    {
        var ex = Assert.ThrowsAny<YamlException>(() => MotelyRunConfig.FromYaml(Head + "must:\n" + must));

        Assert.Equal(line, ex.Start.Line);
        Assert.Contains(message, ex.Message);
    }
}
