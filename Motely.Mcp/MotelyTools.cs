using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motely.Mcp;

/// <summary>
/// The MCP tool surface. These methods are thin adapters: each calls a service that can be tested
/// without a protocol host, then returns the one text+JSON result shape every tool shares.
/// </summary>
[McpServerToolType]
public static class MotelyTools
{
    [Description(MotelyToolDescriptions.SeedValidate)]
    public static CallToolResult SeedValidate(
        [Description("JAML filter text, a path to a filter file, or a bare name in Filters/ (e.g. \"longCola\").")]
        string filter
    )
    {
        if (!FilterToolService.TryLoad(filter, out var loaded, out var error))
            return ToolResults.Error(error!);

        return ToolResults.Ok(
            $"ok: {loaded!.Label} ({loaded.Config.Deck} {loaded.Config.Stake}) — " +
            $"{loaded.Config.Must.Count} must, {loaded.Config.Should.Count} should, " +
            $"{loaded.Config.MustNot.Count} mustNot; the search plan builds.",
            new
            {
                ok = true,
                name = loaded.Label,
                deck = loaded.Config.Deck.ToString(),
                stake = loaded.Config.Stake.ToString(),
                must = loaded.Config.Must.Count,
                should = loaded.Config.Should.Count,
                mustNot = loaded.Config.MustNot.Count,
            }
        );
    }

    [Description(MotelyToolDescriptions.SeedSearch)]
    public static CallToolResult SeedSearch(
        [Description("JAML filter text, a filter file path, or a bare name in Filters/ (e.g. \"longCola\").")]
        string filter,
        [Description("Which seeds to try: replay (default), seeds, keyword, random, sequential.")]
        string mode = "replay",
        [Description("Comma-separated seeds for mode \"seeds\", or a seed file path, e.g. \"A1B2C3D4,E5F6G7H8\".")]
        string? seeds = null,
        [Description("Seed word for mode \"keyword\", e.g. \"COLA\".")]
        string? keyword = null,
        [Description("How many random seeds for mode \"random\".")]
        int? randomCount = null,
        [Description("Stop after this many matches (default 100, max 10000).")]
        int collect = SearchToolService.DefaultCollect
    ) => SearchToolService.Execute(filter, mode, seeds, keyword, randomCount, collect);

    [Description(MotelyToolDescriptions.SoulOrder)]
    public static CallToolResult SoulOrder(
        [Description("The Balatro seed to inspect, e.g. \"JNN5KG71\" (0 folds to O).")]
        string seed,
        [Description("Deck name from the filter vocabulary, e.g. \"Ghost\".")]
        string deck = "Red",
        [Description("Stake name from the filter vocabulary, e.g. \"White\".")]
        string stake = "White",
        [Description("Last ante walked; default 39 checks the whole run.")]
        int maxAnte = 39,
        [Description("Comma-separated held jokers, e.g. \"Showman\" (its JAML `with:` block).")]
        string? heldJokers = null
    ) => SoulOrderToolService.Execute(seed, deck, stake, maxAnte, heldJokers);

    [Description(MotelyToolDescriptions.BestSeed)]
    public static CallToolResult BestSeed(
        [Description("JAML filter text, a filter file path, or a bare name in Filters/ (e.g. \"longCola\").")]
        string filter,
        [Description("A seed file, motely CSV, or a comma-separated list of seeds.")]
        string seeds,
        [Description("How many ranked seeds to return (default 10, max 100).")]
        int top = 10
    ) => BestSeedToolService.Execute(filter, seeds, top);

    [Description(MotelyToolDescriptions.AnalyzeSeed)]
    public static CallToolResult AnalyzeSeed(
        [Description("The Balatro seed to inspect, e.g. \"GSNQDOAA\" (0 folds to O).")]
        string seed,
        [Description("Optional JAML filter whose antes, shop slots and `with:` drive the exact window.")]
        string? filter = null,
        [Description("Deck name when no filter is given, e.g. \"Ghost\".")]
        string deck = "Red",
        [Description("Stake name when no filter is given, e.g. \"White\".")]
        string stake = "White",
        [Description("Last ante when no filter is given.")]
        int maxAnte = 8,
        [Description("Shop cards per ante when no filter is given (1-100, default 12).")]
        int shopLimit = 12
    ) => AnalyzeSeedToolService.Execute(seed, filter, deck, stake, maxAnte, shopLimit);
}
