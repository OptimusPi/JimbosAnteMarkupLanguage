using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motely.Mcp;

// Motely.Mcp: the Motely seed engine over MCP stdio. stdout carries the protocol only; nothing
// here writes to it, and the engine never does.

var tools = typeof(MotelyTools)
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
    .Select(m => McpServerTool.Create(m, target: null, new McpServerToolCreateOptions
    {
        Name = m.GetCustomAttribute<McpServerToolAttribute>()!.Name,
        SerializerOptions = McpEngine.Json,
    }))
    .ToList();

var options = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "motely", Version = typeof(MotelyTools).Assembly.GetName().Version?.ToString() ?? "0" },
    ServerInstructions =
        "Motely seed engine (Balatro). Write filters in JAML; validate_jaml first, then search (returns a job id), "
        + "then search_status. score_seeds and explain_seed check single seeds. vocabulary lists the engine's names.",
    ToolCollection = [.. tools],
};

await using var server = McpServer.Create(new StdioServerTransport("motely"), options);
await server.RunAsync();

[McpServerToolType]
public static class MotelyTools
{
    private static string Ok(JsonNode node) => node.ToJsonString(McpEngine.Json);

    private static string Fail(string message) => new JsonObject { ["ok"] = false, ["error"] = message }.ToJsonString();

    private static JsonNode? DefaultSources(Motely.Filters.JamlConfig config)
    {
        var uses = McpEngine.DefaultSourcesUsed(config);
        return uses.Count == 0 ? null : JsonSerializer.SerializeToNode(uses, McpEngine.Json);
    }

    private static string LoadFailure(JamlLoadError e) =>
        new JsonObject { ["ok"] = false, ["error"] = e.Message, ["line"] = e.Line, ["column"] = e.Column }.ToJsonString();

    [McpServerTool(Name = "validate_jaml", ReadOnly = true)]
    [Description("Load a JAML filter with the engine's own loader. Returns ok, or the engine's error with its line number. Also lists clauses written without sources: and the sources the engine reads for them.")]
    public static string ValidateJaml([Description("The filter, as JAML text.")] string jaml)
    {
        if (!McpEngine.TryLoad(jaml, out var config, out var error))
            return LoadFailure(error!);
        var result = new JsonObject
        {
            ["ok"] = true,
            ["name"] = config.Name,
            ["deck"] = config.Deck.ToString(),
            ["stake"] = config.Stake.ToString(),
            ["must"] = new JsonArray([.. config.Must.Select((c, i) => (JsonNode)McpEngine.Label(c, i))]),
            ["should"] = new JsonArray([.. config.Should.Select((c, i) => (JsonNode)McpEngine.Label(c, i))]),
            ["mustNot"] = new JsonArray([.. config.MustNot.Select((c, i) => (JsonNode)McpEngine.Label(c, i))]),
        };
        if (DefaultSources(config) is { } sources)
            result["defaultSources"] = sources;
        return Ok(result);
    }

    [McpServerTool(Name = "search")]
    [Description("Start a background seed search and return a job id at once. Poll search_status with the id. mode: sequential | random | keyword | aesthetic | seed_list.")]
    public static string Search(
        [Description("The filter, as JAML text.")] string jaml,
        [Description("sequential | random | keyword | aesthetic | seed_list")] string mode = "sequential",
        [Description("count (random), keyword and padding (keyword), aesthetic and padding (aesthetic), seeds (seed_list), startSeed/stopSeed (sequential), threads (default cores minus 2), collect (stop after N matches), minScore, save + filterPath (write top seeds into that filter file).")]
            SearchOptions? options = null
    )
    {
        if (!McpEngine.TryLoad(jaml, out var config, out var error))
            return LoadFailure(error!);
        SearchJob job;
        try
        {
            job = SearchJobs.Start(config, mode, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
        var result = new JsonObject
        {
            ["ok"] = true,
            ["jobId"] = job.Id,
            ["mode"] = job.Mode,
            ["threads"] = job.Threads,
            ["deck"] = config.Deck.ToString(),
            ["stake"] = config.Stake.ToString(),
        };
        if (DefaultSources(config) is { } sources)
            result["defaultSources"] = sources;
        return Ok(result);
    }

    [McpServerTool(Name = "search_status", ReadOnly = true)]
    [Description("A search's progress: seeds searched, speed, matches so far, and the top 50 matches by score with per-clause tallies. Every match shown is re-scored clause by clause, and any that fails is flagged MISMATCH.")]
    public static string SearchStatus([Description("The id search returned.")] string jobId) =>
        SearchJobs.Get(jobId) is { } job ? Ok(job.Status()) : Fail($"no job '{jobId}'.");

    [McpServerTool(Name = "search_cancel")]
    [Description("Stop a running search. Its matches so far stay readable with search_status.")]
    public static async Task<string> SearchCancel([Description("The id search returned.")] string jobId)
    {
        if (SearchJobs.Get(jobId) is not { } job)
            return Fail($"no job '{jobId}'.");
        job.Cancel();
        await Task.WhenAny(job.Completion, Task.Delay(TimeSpan.FromSeconds(10)));
        return Ok(job.Status());
    }

    [McpServerTool(Name = "score_seeds", ReadOnly = true)]
    [Description("Check seeds against a filter, clause by clause. Each seed: pass or fail; a fail names the must clause(s) that failed or the mustNot that matched; a pass gives the score and the per-should-clause tallies.")]
    public static string ScoreSeeds(
        [Description("The filter, as JAML text. Its deck and stake are used.")] string jaml,
        [Description("The seeds to check.")] string[] seeds
    )
    {
        if (!McpEngine.TryLoad(jaml, out var config, out var error))
            return LoadFailure(error!);
        var result = new JsonObject
        {
            ["ok"] = true,
            ["deck"] = config.Deck.ToString(),
            ["stake"] = config.Stake.ToString(),
            ["seeds"] = JsonSerializer.SerializeToNode(McpEngine.ScoreSeeds(config, seeds ?? []), McpEngine.Json),
        };
        if (DefaultSources(config) is { } sources)
            result["defaultSources"] = sources;
        return Ok(result);
    }

    [McpServerTool(Name = "explain_seed", ReadOnly = true)]
    [Description("What a seed holds, per ante: boss, voucher, small and big blind tags, the shop with stickers and editions, every booster pack and its cards, and the order legendaries come out of Souls.")]
    public static string ExplainSeed(
        [Description("The seed.")] string seed,
        [Description("Deck (see vocabulary decks). Default Red.")] string deck = "Red",
        [Description("Stake (see vocabulary stakes). Default White.")] string stake = "White",
        [Description("Antes to show, 0-39. Default 1-8.")] int[]? antes = null,
        [Description("Sections to show: boss, voucher, tags, shop, packs, legendary. Default all.")] string[]? include = null,
        [Description("Shop items per ante. 0 (default) shows 15 for antes 0-1 and 50 after.")] int shopSlots = 0
    )
    {
        if (!Enum.TryParse<MotelyDeck>(deck, true, out var d))
            return Fail($"unknown deck '{deck}'; known: {string.Join(", ", Enum.GetNames<MotelyDeck>())}.");
        if (!Enum.TryParse<MotelyStake>(stake, true, out var s))
            return Fail($"unknown stake '{stake}'; known: {string.Join(", ", Enum.GetNames<MotelyStake>())}.");
        try
        {
            return Ok(McpEngine.ExplainSeed(seed, d, s, antes, include, shopSlots));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    [McpServerTool(Name = "vocabulary", ReadOnly = true)]
    [Description("Names straight from the engine's enums. kind: jokers, tags, vouchers, spectrals, tarots, planets, bosses, stickers, editions, decks, stakes.")]
    public static string Vocabulary([Description("Which list.")] string kind)
    {
        try
        {
            return Ok(new JsonObject
            {
                ["kind"] = kind,
                ["names"] = new JsonArray([.. McpEngine.Vocabulary(kind).Select(n => (JsonNode)n)]),
            });
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
    }
}
