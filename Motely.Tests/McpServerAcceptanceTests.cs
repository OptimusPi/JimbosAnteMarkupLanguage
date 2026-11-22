using System.Text.Json.Nodes;
using Motely.Mcp;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// The MCP server's acceptance seeds, checked through the same engine bridge its tools call
/// (<see cref="McpEngine"/>, <see cref="SearchJobs"/>). Each passing seed has a twin that moves
/// one clause off what the seed holds, so a pass cannot be a filter that passes everything.
/// </summary>
public sealed class McpServerAcceptanceTests(ITestOutputHelper output)
{
    private const string OwnFilter = """
        name: ownnl4g9
        deck: Ghost
        stake: White
        must:
          - spectralCard: Ankh
            antes: [1]
            sources:
              shopItems: [2, 3]
          - joker: [Blueprint, Brainstorm, InvisibleJoker]
            antes: [1, 2]
          - legendaryJoker: Perkeo
            antes: [1, 2, 3]
          - tag: NegativeTag
            antes: [8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]
        """;

    private const string B13Filter = """
        name: b13zi3ok
        deck: Black
        stake: Black
        must:
          - tag: CouponTag
            antes: [1]
            rolls: [0]
          - joker: Madness
            stickers: [Eternal]
            antes: [1]
            sources:
              shopItems: [0]
          - joker: Brainstorm
            stickers: [Eternal]
            antes: [1]
            sources:
              boosterPacks: [0]
          - spectralCard: Ankh
            antes: [1]
            sources:
              boosterPacks: [1]
        """;

    private const string PerkeoAnte1 = """
        deck: Ghost
        stake: White
        must:
          - legendaryJoker: Perkeo
            antes: [1]
        """;

    private static JamlConfig Load(string jaml)
    {
        Assert.True(McpEngine.TryLoad(jaml, out var config, out var error), error?.Message);
        return config;
    }

    private static SeedVerdict Score(string jaml, string seed) => Assert.Single(McpEngine.ScoreSeeds(Load(jaml), [seed]));

    /// <summary>The engine's own search over a one-seed list: what search mode seed_list reports.</summary>
    private static async Task<bool> SearchFinds(string jaml, string seed)
    {
        var job = SearchJobs.Start(Load(jaml), "seed_list", new SearchOptions { Seeds = [seed], Threads = 1 });
        await job.Completion;
        return job.MatchCount == 1 && job.Top(1).Single().Seed == seed;
    }

    [Fact]
    public async Task Ownnl4g9_GhostWhite_Passes()
    {
        var verdict = Score(OwnFilter, "OWNNL4G9");
        Assert.True(verdict.Pass, $"failed: {string.Join(", ", verdict.FailedMust?.Select(f => f.Label) ?? [])}");
        Assert.True(await SearchFinds(OwnFilter, "OWNNL4G9"));
    }

    [Fact]
    public async Task Ownnl4g9_PerkeoOnlyInAnte4_Fails()
    {
        string twin = OwnFilter.Replace("legendaryJoker: Perkeo\n    antes: [1, 2, 3]", "legendaryJoker: Perkeo\n    antes: [4]");
        Assert.NotEqual(OwnFilter, twin);
        var verdict = Score(twin, "OWNNL4G9");
        Assert.False(verdict.Pass);
        Assert.Equal("must[2]", Assert.Single(verdict.FailedMust!).Path);
        Assert.False(await SearchFinds(twin, "OWNNL4G9"));
    }

    [Fact]
    public async Task B13zi3ok_BlackBlack_Passes()
    {
        var verdict = Score(B13Filter, "B13ZI3OK");
        Assert.True(verdict.Pass, $"failed: {string.Join(", ", verdict.FailedMust?.Select(f => f.Label) ?? [])}");
        Assert.True(await SearchFinds(B13Filter, "B13ZI3OK"));
    }

    [Theory]
    [InlineData("shopItems: [0]", "shopItems: [1]", "must[1]")] // Madness is in shop slot 0, not 1
    [InlineData("boosterPacks: [1]", "boosterPacks: [0]", "must[3]")] // Ankh is in pack 1, not 0
    [InlineData("rolls: [0]", "rolls: [1]", "must[0]")] // Coupon Tag is on the small blind
    public async Task B13zi3ok_MovedClause_Fails(string from, string to, string failingPath)
    {
        string twin = B13Filter.Replace(from, to);
        Assert.NotEqual(B13Filter, twin);
        var verdict = Score(twin, "B13ZI3OK");
        Assert.False(verdict.Pass);
        Assert.Equal(failingPath, Assert.Single(verdict.FailedMust!).Path);
        Assert.False(await SearchFinds(twin, "B13ZI3OK"));
    }

    [Fact]
    public void B13zi3ok_ExplainSeed_ShowsWhatTheFilterNames()
    {
        var explained = McpEngine.ExplainSeed("B13ZI3OK", MotelyDeck.Black, MotelyStake.Black, [1], null, shopSlots: 4);
        var ante = explained["antes"]!.AsArray().Single()!;
        Assert.Equal("CouponTag", (string?)ante["smallBlindTag"]);
        Assert.Equal("0: Eternal Madness", (string?)ante["shop"]![0]);
        var packs = ante["packs"]!.AsArray();
        Assert.Contains("Eternal Brainstorm", packs[0]!["cards"]!.AsArray().Select(c => (string?)c));
        Assert.Contains("Ankh", packs[1]!["cards"]!.AsArray().Select(c => (string?)c));
    }

    [Fact]
    public async Task J2eze2j_GhostWhite_FailsPerkeoInAnte1()
    {
        var verdict = Score(PerkeoAnte1, "J2EZE2J");
        Assert.False(verdict.Pass);
        var failed = Assert.Single(verdict.FailedMust!);
        Assert.Equal("must[0]", failed.Path);
        Assert.Equal("Perkeo in ante 1", failed.Label);
        Assert.False(await SearchFinds(PerkeoAnte1, "J2EZE2J"));
    }

    [Fact]
    public void J2eze2j_PositiveControl_TheSeedIsWalked()
    {
        // The 7-character seed is read: its real ante-1 small blind tag passes, and no Soul in
        // antes 1-3 gives Perkeo.
        var explained = McpEngine.ExplainSeed("J2EZE2J", MotelyDeck.Ghost, MotelyStake.White, [1, 2, 3], null);
        string tag = (string)explained["antes"]![0]!["smallBlindTag"]!;
        Assert.True(Score($"deck: Ghost\nstake: White\nmust:\n  - tag: {tag}\n    antes: [1]\n    rolls: [0]\n", "J2EZE2J").Pass);
        Assert.DoesNotContain(
            explained["legendaryOrder"]!.AsArray(),
            l => (int)l!["ante"]! == 1 && ((string)l["joker"]!).EndsWith("Perkeo")
        );
    }

    [Fact]
    public void Ownnl4g9_ExplainSeed_PastAnte8_ShowsTheNegativeTag()
    {
        int[] antes = [.. Enumerable.Range(8, 13)];
        var rows = McpEngine.ExplainSeed("OWNNL4G9", MotelyDeck.Ghost, MotelyStake.White, antes, ["tags"])["antes"]!.AsArray();
        Assert.Equal(antes, rows.Select(r => (int)r!["ante"]!));
        Assert.Contains(rows, r => (string?)r!["smallBlindTag"] == "NegativeTag" || (string?)r!["bigBlindTag"] == "NegativeTag");
    }

    [Fact]
    public async Task Save_WritesSeedsOnlyWhenAsked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mcp-save-{Guid.NewGuid():N}.yaml");
        try
        {
            File.WriteAllText(path, B13Filter + "\n");
            byte[] before = File.ReadAllBytes(path);

            var plain = SearchJobs.Start(Load(B13Filter), "seed_list", new SearchOptions { Seeds = ["B13ZI3OK"], FilterPath = path });
            await plain.Completion;
            Assert.Equal(before, File.ReadAllBytes(path));

            var saving = SearchJobs.Start(Load(B13Filter), "seed_list", new SearchOptions { Seeds = ["B13ZI3OK"], FilterPath = path, Save = true });
            await saving.Completion;
            Assert.StartsWith("saved 1 seed", saving.SaveResult);
            Assert.Equal(["B13ZI3OK"], JamlConfig.FromFile(path).Seeds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidateJaml_ReturnsTheEnginesErrorWithItsLine()
    {
        Assert.False(McpEngine.TryLoad("deck: Ghost\nmust:\n  - joker: Blueprnt\n", out _, out var error));
        Assert.Equal(3, error!.Line);
        Assert.StartsWith("JAML line 3:", error.Message);
    }

    [Fact]
    public void ClauseWithoutSources_ReportsTheDefaultTheEngineUsed()
    {
        var uses = McpEngine.DefaultSourcesUsed(Load(OwnFilter));
        // must[0] wrote sources:, must[3] is a tag (no sources at all).
        Assert.Equal(["must[1]", "must[2]"], uses.Select(u => u.Path));
        var joker = uses[0];
        Assert.Equal("joker", joker.Kind);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], joker.Sources["shopItems"]!.AsArray().Select(n => (int)n!));
        Assert.Contains("boosterPacks", joker.NotCounted);
        Assert.Equal("legendaryJoker", uses[1].Kind);
    }

    [Fact]
    public void Vocabulary_ComesFromTheEnums()
    {
        Assert.Equal(Enum.GetNames<MotelyJoker>(), McpEngine.Vocabulary("jokers"));
        Assert.Contains("Eternal", McpEngine.Vocabulary("stickers"));
        Assert.Throws<ArgumentException>(() => McpEngine.Vocabulary("nope"));
    }

    [Fact]
    public async Task RandomSearch_3M_EveryMatchPassesScoreSeeds()
    {
        const int count = 3_000_000;
        var job = SearchJobs.Start(Load(OwnFilter), "random", new SearchOptions { Count = count });
        await job.Completion;

        Assert.Equal("finished", job.State);
        Assert.Equal(count, job.Search.TotalSeedsSearched);
        var matches = job.Top(SearchJob.KeepMatches);
        Assert.Equal(job.MatchCount, matches.Count);

        var rechecked = job.Recheck(matches);
        var bad = rechecked.Where(r => !SearchJob.Agrees(r.Match, r.Verdict)).Select(r => r.Match.Seed).ToList();
        output.WriteLine(
            $"random {count:N0}: {job.Search.TotalSeedsSearched:N0} searched in {job.Search.ElapsedMs:N0} ms, "
                + $"{job.Search.SeedsPerSecond:N0} seeds/s, {job.MatchCount} match(es): {string.Join(", ", matches.Select(m => m.Seed))}"
        );
        Assert.Empty(bad);

        // The status the search_status tool returns carries the same recheck.
        JsonObject status = job.Status();
        Assert.DoesNotContain("mismatches", status.Select(p => p.Key));
    }
}
