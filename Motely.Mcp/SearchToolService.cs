using Motely.Filters;
using ModelContextProtocol.Protocol;

namespace Motely.Mcp;

/// <summary>
/// The native counterpart of Motely.Web's seed_search tool: same mode names, same argument rules,
/// same summary text. Only the engine differs (SIMD here, WASM in the browser).
/// </summary>
internal static class SearchToolService
{
    public const int DefaultCollect = 100;
    public const int MaxCollect = 10_000;

    private static readonly string[] Modes = ["replay", "seeds", "keyword", "random", "sequential"];
    private static readonly char[] DefaultPadding = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    public static CallToolResult Execute(
        string? filter,
        string? mode,
        string? seeds,
        string? keyword,
        int? randomCount,
        int collect
    )
    {
        if (!FilterToolService.TryLoad(filter, out var loaded, out var error))
            return ToolResults.Error(error!);

        string modeText = string.IsNullOrWhiteSpace(mode) ? "replay" : mode!.Trim().ToLowerInvariant();
        if (Array.IndexOf(Modes, modeText) < 0)
            return ToolResults.Error(
                $"Error: unknown mode '{mode}'. Use replay, seeds, keyword, random, or sequential."
            );

        bool hasSeeds = seeds is not null;
        bool hasKeyword = keyword is not null;
        bool hasRandom = randomCount.HasValue;
        int selectorCount = (hasSeeds ? 1 : 0) + (hasKeyword ? 1 : 0) + (hasRandom ? 1 : 0);
        if (selectorCount > 1)
        {
            string given = string.Join(
                " and ",
                new[] { hasSeeds ? "seeds" : null, hasKeyword ? "keyword" : null, hasRandom ? "randomCount" : null }
                    .Where(static value => value is not null)
            );
            return ToolResults.Error(
                $"Error: pass exactly one of seeds, keyword or randomCount (you gave {given}). " +
                "Set mode to match: seeds → mode \"seeds\", keyword → mode \"keyword\", " +
                "randomCount → mode \"random\"."
            );
        }

        string? wanted = modeText switch
        {
            "seeds" => "seeds",
            "keyword" => "keyword",
            "random" => "randomCount",
            _ => null,
        };
        string? got = hasSeeds ? "seeds" : hasKeyword ? "keyword" : hasRandom ? "randomCount" : null;
        if (wanted != got)
        {
            string hint = (wanted, modeText) switch
            {
                (null, "replay") =>
                    "mode \"replay\" replays the filter's own `seeds:` block and takes no selector — " +
                    "remove seeds/keyword/randomCount, or change mode to match the selector you passed.",
                (null, _) =>
                    "mode \"sequential\" sweeps the seed space from the start and takes no selector — " +
                    "remove seeds/keyword/randomCount, or change mode to match the selector you passed.",
                ("seeds", _) => "mode \"seeds\" searches exactly the seeds you list — pass seeds \"A1B2C3D4,E5F6G7H8\".",
                ("keyword", _) => "mode \"keyword\" searches seeds containing a word — pass keyword \"COLA\".",
                _ => "mode \"random\" tries random seeds — pass randomCount (integer ≥ 1), e.g. 1000.",
            };
            return ToolResults.Error($"Error: {hint}");
        }

        if (randomCount is < 1)
            return ToolResults.Error("Error: randomCount must be an integer ≥ 1 — how many random seeds to try.");
        if (collect < 1 || collect > MaxCollect)
            return ToolResults.Error($"Error: collect must be an integer between 1 and {MaxCollect}.");

        string[]? listedSeeds = null;
        string? normalizedKeyword = null;
        if (hasSeeds && !SeedListService.TryRead(seeds, out listedSeeds, out error))
            return ToolResults.Error(error!);
        if (hasKeyword)
        {
            if (!CliSearchMode.TryNormalizeSeed(keyword!, out normalizedKeyword) || normalizedKeyword!.Length == 0)
                return ToolResults.Error(
                    $"Error: keyword '{keyword}' is not part of a seed (1–8 characters of 1-9, A-Z; 0 folds to O)."
                );
        }

        try
        {
            JamlConfig config = loaded!.Config;
            var settings = MotelySearchBuilder.CreateSettings(config)
                .WithThreadCount(1)
                .WithQuietMode(true);

            switch (modeText)
            {
                case "replay":
                    settings = settings.WithSeedList([.. config.Seeds]);
                    break;
                case "seeds":
                    settings = settings.WithSeedList(listedSeeds!);
                    break;
                case "keyword":
                    settings = settings.WithKeywordSearch([normalizedKeyword!], DefaultPadding);
                    break;
                case "random":
                    settings = settings.WithRandomSearch(randomCount!.Value);
                    break;
                default:
                    settings = settings
                        .WithSequentialSearch()
                        .WithBatchCharacterCount(3)
                        .WithStartBatchIndex(0)
                        .WithEndBatchIndex(70);
                    break;
            }

            settings = settings.StopAfter(collect);
            var matches = new List<string>();
            settings = settings.SeedScoreDesc is not null
                ? settings.WithScoredResultCallback(result => matches.Add(result.Seed))
                : settings.WithSeedMatchCallback(matches.Add);

            using var search = settings.Start();
            search.AwaitCompletion();

            string summary =
                $"{loaded.Label}: {McpFormat.Number(search.MatchingSeeds)} matched / " +
                $"{McpFormat.Number(search.TotalSeedsSearched)} searched in " +
                $"{McpFormat.Duration(search.ElapsedMs)} " +
                $"({McpFormat.Number((long)Math.Round(search.SeedsPerSecond))} seeds/s).";
            string text = matches.Count > 0
                ? $"{summary}\nMatches: {string.Join(", ", matches.Select(static seed => seed.Length == 0 ? "\"\"" : seed))}"
                : $"{summary}\nNo seeds matched.";

            return ToolResults.Ok(
                text,
                new
                {
                    ok = true,
                    filter = loaded.Label,
                    mode = modeText,
                    matched = search.MatchingSeeds,
                    searched = search.TotalSeedsSearched,
                    elapsedMs = search.ElapsedMs,
                    seedsPerSecond = search.SeedsPerSecond,
                    matches = matches.ToArray(),
                }
            );
        }
        catch (Exception ex)
        {
            return ToolResults.Error($"Error: the search failed: {ex.GetBaseException().Message}");
        }
    }
}
