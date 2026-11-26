using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motely.Analysis;
using Motely.Filters;
using Motely.Mcp;

namespace Motely.Tests;

/// <summary>
/// The MCP tools are public, host-independent adapters. These tests call them directly — no stdio
/// host, no network — so the bot contract is pinned the same way the WebMCP text contracts are.
/// </summary>
public sealed class McpToolTests
{
    private static string RepoRoot => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")
    );

    private static string Filter(string name) => Path.Combine(RepoRoot, "Filters", name + ".yaml");

    private static string Text(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static JsonElement Structured(CallToolResult result) =>
        result.StructuredContent ?? throw new InvalidOperationException("No structured content.");

    [Fact]
    public void ToolNames_MatchTheWebMcpContract()
    {
        Assert.Equal("seed_validate", MotelyToolNames.SeedValidate);
        Assert.Equal("seed_search", MotelyToolNames.SeedSearch);
        Assert.Equal("soul_order", MotelyToolNames.SoulOrder);
        Assert.Equal("best_seed", MotelyToolNames.BestSeed);
        Assert.Equal("analyze_seed", MotelyToolNames.AnalyzeSeed);
    }

    [Fact]
    public void SeedValidate_FindsABareFilterNameAndCountsClauses()
    {
        // A bare name must resolve from the server binary upward, not just the current directory.
        var result = MotelyTools.SeedValidate("ColaLit");

        Assert.False(result.IsError);
        Assert.Contains("ok: ColaLit (Ghost White)", Text(result));
        Assert.Contains("5 must, 3 should", Text(result));
        Assert.Equal("ColaLit", Structured(result).GetProperty("name").GetString());
    }

    [Fact]
    public void SeedSearch_RejectsAWrongSelectorWithAUsefulHint()
    {
        var result = MotelyTools.SeedSearch(Filter("ColaLit"), mode: "seeds");

        Assert.True(result.IsError);
        Assert.Contains("mode \"seeds\" searches exactly the seeds you list", Text(result));
    }

    [Fact]
    public void BestSeed_RefusesAPathOutsideTheAllowedAreas()
    {
        string outsideDir = Path.GetFullPath(
            Path.Combine(RepoRoot, "..", $"motely-outside-{Guid.NewGuid():N}")
        );
        Directory.CreateDirectory(outsideDir);
        try
        {
            string path = Path.Combine(outsideDir, "seeds.txt");
            File.WriteAllText(path, "ALEEB");
            const string jaml = """
                name: path-boundary
                deck: Red
                stake: White
                should:
                  - boss: Any
                    antes: [1]
                    score: 1
                """;

            var result = MotelyTools.BestSeed(jaml, path, top: 1);

            Assert.True(result.IsError);
            Assert.Contains("outside the allowed search areas", Text(result));
        }
        finally
        {
            Directory.Delete(outsideDir, recursive: true);
        }
    }

    [Fact]
    public void SoulOrder_ReportsTheAntelessFaceSequence()
    {
        var result = MotelyTools.SoulOrder("JNN5KG71", "Ghost", "White", maxAnte: 39);

        Assert.False(result.IsError);
        string text = Text(result);
        Assert.Contains("Perkeo → Chicot → Canio", text);
        Assert.Contains("Perkeo → Perkeo: no", text);
        var json = Structured(result);
        Assert.True(json.GetProperty("anteless").GetBoolean());
        Assert.Equal(3, json.GetProperty("count").GetInt32());
        Assert.False(json.GetProperty("perkeoThenPerkeo").GetBoolean());
        Assert.Equal("Perkeo", json.GetProperty("order")[0].GetProperty("joker").GetString());
    }

    [Fact]
    public void BestSeed_RanksAListedSeedPile()
    {
        const string jaml = """
            name: rank-test
            deck: Red
            stake: White
            should:
              - boss: Any
                antes: [1]
                score: 1
            """;
        var result = MotelyTools.BestSeed(jaml, "C3DW94B8,3MLDLU2W", top: 2);

        Assert.False(result.IsError);
        string text = Text(result);
        Assert.Contains("best seed for rank-test from 2 listed seeds", text);
        var json = Structured(result);
        Assert.Equal(2, json.GetProperty("searched").GetInt64());
        Assert.Equal(2, json.GetProperty("matched").GetInt64());
        Assert.Equal("C3DW94B8", json.GetProperty("best").GetProperty("seed").GetString());
        Assert.True(json.GetProperty("top").GetArrayLength() >= 1);
    }

    [Fact]
    public void AnalyzeSeed_UsesTheFilterTextAndKeepsSoulOrderAnteless()
    {
        var result = MotelyTools.AnalyzeSeed("GSNQDOAA", filter: "ColaLit");

        Assert.False(result.IsError);
        var json = Structured(result);
        Assert.Equal("GSNQDOAA", json.GetProperty("seed").GetString());
        Assert.Equal("ColaLit", json.GetProperty("filter").GetString());
        Assert.True(json.GetProperty("antelessSoulOrder").GetArrayLength() >= 1);
        Assert.True(json.GetProperty("antes").GetArrayLength() >= 1);
    }

    [Fact]
    public void AnalyzeSeed_ShowsTheShopTheFiltersWithBlockReads()
    {
        const string jaml = """
            deck: Red
            seeds: [PIFREAKS]
            must:
              - tarotCard: any
                antes: [1]
                with: { vouchers: [TarotMerchant, TarotTycoon] }
            """;

        var result = MotelyTools.AnalyzeSeed("PIFREAKS", filter: jaml, shopLimit: 12);
        Assert.False(result.IsError);
        var reported = Shop(Structured(result), 1);

        var config = JamlConfig.FromJaml(jaml);
        var with = JamlConfigWith.Find(config);
        Assert.NotNull(with);
        var expected = Shop(
            Assert.Single(MotelyJamlyzer.Analyze(config, eventRolls: 0, shopSlots: 12, with: with)),
            1
        );
        var plain = Shop(
            Assert.Single(MotelyJamlyzer.Analyze(config, eventRolls: 0, shopSlots: 12)),
            1
        );

        Assert.Equal(expected, reported);
        Assert.NotEqual(plain, reported);
    }

    [Fact]
    public void SeedValidate_FindsABareJamlName()
    {
        // A bare name must try every filter extension, not only .yaml.
        var result = MotelyTools.SeedValidate("Whimsy_Dicetricks314");

        Assert.False(result.IsError);
        Assert.Contains("Whimsy Dicetricks", Text(result));
    }

    [Fact]
    public void SeedValidate_SaysWhenANameHasNoFile()
    {
        var result = MotelyTools.SeedValidate("no_such_filter_xyz");

        Assert.True(result.IsError);
        Assert.Contains("no filter file found", Text(result));
        Assert.Contains("paste the JAML text", Text(result));
    }

    [Fact]
    public void SeedSearch_ReplayWithoutASeedsBlockExplainsWhatToDo()
    {
        const string jaml = """
            name: no-seeds
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
            """;

        var result = MotelyTools.SeedSearch(jaml, mode: "replay");

        Assert.True(result.IsError);
        Assert.Contains("has no `seeds:` block to replay", Text(result));
        Assert.Contains("mode \"seeds\"", Text(result));
    }

    [Fact]
    public void BestSeed_SaysWhenTheFilterScoresNothing()
    {
        const string jaml = """
            name: no-should
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
            """;

        var result = MotelyTools.BestSeed(jaml, "C3DW94B8,3MLDLU2W", top: 2);

        Assert.False(result.IsError);
        Assert.Contains("has no `should:` clauses", Text(result));
        Assert.False(Structured(result).GetProperty("scored").GetBoolean());
    }

    private static string[] Shop(JsonElement json, int ante) =>
        json.GetProperty("antes").EnumerateArray()
            .Single(entry => entry.GetProperty("ante").GetInt32() == ante)
            .GetProperty("shop").EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

    private static string[] Shop(MotelyJamlyzerSeedResult result, int ante) =>
        result.Antes.Single(entry => entry.Ante == ante).ShopItems
            .Select(item => item.ToString())
            .ToArray();
}
