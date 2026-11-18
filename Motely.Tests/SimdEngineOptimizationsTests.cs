using Motely.Filters;

namespace Motely.Tests;

public sealed class SimdEngineOptimizationsTests
{
    private static readonly string[] TestSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF", "KHTW99TC", "1YU11111",
        "11111111", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "EEEEEEEE", "FFFFFFFF",
    ];

    private static HashSet<string> RunMust(
        MotelyClause clause,
        MotelyDeck deck = MotelyDeck.Red,
        MotelyStake stake = MotelyStake.White
    )
    {
        var config = new JamlConfig
        {
            Id = "simd-opt-must",
            Deck = deck,
            Stake = stake,
        };
        config.Must.Add(clause);

        var hits = new HashSet<string>(StringComparer.Ordinal);
        using var search = MotelySearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(TestSeeds, TestSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => hits.Add(s))
            .Start();
        search.AwaitCompletion();
        return hits;
    }

    private static HashSet<string> RunShould(
        MotelyClause clause,
        MotelyDeck deck = MotelyDeck.Red,
        MotelyStake stake = MotelyStake.White
    )
    {
        clause.Score = 1;
        var config = new JamlConfig
        {
            Id = "simd-opt-should",
            Deck = deck,
            Stake = stake,
        };
        config.Should.Add(clause);

        var hits = new HashSet<string>(StringComparer.Ordinal);
        var runState = deck.GetDefaultRunState();
        using var search = MotelySearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(TestSeeds, TestSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                if (ClauseScoring.ClauseMeetsMinForFilter(ref ctx, clause))
                    hits.Add(ctx.GetSeed());
                return 1;
            })
            .Start();
        search.AwaitCompletion();
        return hits;
    }

    [Fact]
    public void SpecialtyAndRawShopSources_AllJokerFamilies_SimdMatchesScalar()
    {
        var allSources = new JokerSourceConfig
        {
            ShopItems = [0, 1],
            BoosterPacks = [0, 1],
            Judgement = [0, 1],
            Wraith = [0, 1],
            RiffRaff = [0, 1],
            RareTag = [0, 1],
            UncommonTag = [0, 1],
            CommonShopJokers = [0, 1],
            UncommonShopJokers = [0, 1],
            RareShopJokers = [0, 1],
            AllShopJokers = [0, 1],
        };

        MotelyClause[] clauses =
        [
            new CommonJokerClause
            {
                Jokers = [MotelyJokerCommon.Joker, MotelyJokerCommon.GreedyJoker, MotelyJokerCommon.GrosMichel],
                Antes = [1, 2],
                Sources = allSources,
            },
            new CommonJokerClause
            {
                Jokers = [],
                Antes = [1],
                Min = 2,
                Sources = allSources,
            },
            new UncommonJokerClause
            {
                Jokers = [MotelyJokerUncommon.Mime, MotelyJokerUncommon.Showman, MotelyJokerUncommon.Dusk],
                Antes = [1, 2],
                Sources = allSources,
            },
            new RareJokerClause
            {
                Jokers = [MotelyJokerRare.Blueprint, MotelyJokerRare.Brainstorm, MotelyJokerRare.DNA],
                Antes = [1, 2],
                Sources = allSources,
            },
            new RareJokerClause
            {
                Jokers = [],
                Antes = [1, 2],
                Min = 2,
                Sources = new JokerSourceConfig
                {
                    Wraith = [0],
                    RareTag = [0],
                    RareShopJokers = [0],
                },
            },
            new JokerClause
            {
                Jokers = [MotelyJoker.Blueprint, MotelyJoker.Brainstorm, MotelyJoker.Mime],
                Antes = [1, 2],
                Sources = allSources,
            },
        ];

        foreach (var clause in clauses)
        {
            var mustHits = RunMust(clause, MotelyDeck.Red, MotelyStake.Gold);
            var scalarHits = RunShould(clause, MotelyDeck.Red, MotelyStake.Gold);
            Assert.Equal(scalarHits, mustHits);
        }
    }

    [Fact]
    public void ShopStreamExclusions_TarotPlanetSpectral_SimdMatchesScalar()
    {
        MotelyClause[] clauses =
        [
            new TarotCardClause
            {
                Tarots = [MotelyTarotCard.TheFool, MotelyTarotCard.TheHermit, MotelyTarotCard.Death],
                Antes = [1, 2],
            },
            new PlanetCardClause
            {
                Planets = [MotelyPlanetCard.Mercury, MotelyPlanetCard.Jupiter, MotelyPlanetCard.Pluto],
                Antes = [1, 2],
            },
            new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.Familiar, MotelySpectralCard.Cryptid, MotelySpectralCard.Immolate],
                Antes = [1, 2],
            },
        ];

        foreach (var clause in clauses)
        {
            Assert.Equal(
                RunShould(clause, MotelyDeck.Ghost, MotelyStake.Gold),
                RunMust(clause, MotelyDeck.Ghost, MotelyStake.Gold)
            );
        }
    }

    [Fact]
    public void LegendaryAndSoulOrder_SimdPrefilterRejectsNonMatchesAndKeeps100PercentRecall()
    {
        var legClause = new LegendaryJokerClause
        {
            Jokers = [MotelyJoker.Perkeo],
            Antes = [1, 2],
            Sources = new LegendaryJokerSourceConfig { ArcanaPacks = [0] },
        };
        Assert.Equal(RunShould(legClause), RunMust(legClause));

        var legAnySourceClause = new LegendaryJokerClause
        {
            Jokers = [MotelyJoker.Perkeo, MotelyJoker.Triboulet],
            Antes = [1, 2, 3, 4],
        };
        Assert.Equal(RunShould(legAnySourceClause), RunMust(legAnySourceClause));

        var soulOrderClause = new LegendarySoulOrderClause
        {
            Entries =
            [
                new SoulOrderEntry { Legendary = MotelyJoker.Triboulet, Antes = [1, 2, 3, 4, 5, 6, 7, 8] },
            ],
            MaxAnte = 8,
        };
        Assert.Equal(RunShould(soulOrderClause), RunMust(soulOrderClause));
    }

    [Fact]
    public void CheapestFirstOrdering_RanksCheapFeaturesBeforeShopPacksAndScalarLast()
    {
        var boss = new BossClause { Bosses = [MotelyBossBlind.ThePlant], Antes = [1] };
        var tag = new TagClause { Tags = [MotelyTag.CharmTag], Antes = [1, 2], Rolls = [0, 1] };
        var leg = new LegendaryJokerClause
        {
            Jokers = [MotelyJoker.Perkeo],
            Antes = [1, 2],
            Sources = new LegendaryJokerSourceConfig { ArcanaPacks = [0] },
        };
        var rareShop = new RareJokerClause
        {
            Jokers = [MotelyJokerRare.Blueprint],
            Antes = [1, 2, 3],
        };
        var standardCardScalar = new StandardCardClause
        {
            Rank = MotelyStandardcardRank.Ace,
            Antes = [1, 2],
        };

        int bossCost = MotelySearchBuilder.EstimateSimdCost(boss);
        int tagCost = MotelySearchBuilder.EstimateSimdCost(tag);
        int legCost = MotelySearchBuilder.EstimateSimdCost(leg);
        int rareCost = MotelySearchBuilder.EstimateSimdCost(rareShop);
        int scalarCost = MotelySearchBuilder.EstimateSimdCost(standardCardScalar);

        Assert.True(bossCost < tagCost);
        Assert.True(tagCost < legCost);
        Assert.True(legCost < rareCost);
        Assert.True(rareCost < scalarCost);
    }
}
