using System.Text.Json;
using ModelContextProtocol.Protocol;
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
}
