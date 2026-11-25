namespace Motely.Mcp;

/// <summary>
/// The names clients see in `tools/list` and `tools/call`. They match Motely.Web's WebMCP tools:
/// short snake_case verbs, no C# method names leaking into the bot contract.
/// </summary>
public static class MotelyToolNames
{
    public const string SeedValidate = "seed_validate";
    public const string SeedSearch = "seed_search";
    public const string SoulOrder = "soul_order";
    public const string BestSeed = "best_seed";
    public const string AnalyzeSeed = "analyze_seed";
}
