using Motely.Filters;
using ModelContextProtocol.Protocol;

namespace Motely.Mcp;

/// <summary>
/// One-call answer to "I have a pile of seeds — which one is best for this filter?". It replays a
/// seed list without the filter's own cutoff, ranks by the JAML's should-scores, and reports the
/// winner plus its tallies. Thread count 1 keeps ties in the caller's list order.
/// </summary>
internal static class BestSeedToolService
{
    public const int MaxTop = 100;

    public static CallToolResult Execute(string? filter, string? seeds, int top)
    {
        if (!FilterToolService.TryLoad(filter, out var loaded, out var error))
            return ToolResults.Error(error!);
        if (!SeedListService.TryRead(seeds, out string[] listed, out error))
            return ToolResults.Error(error!);
        if (top is < 1 or > MaxTop)
            return ToolResults.Error($"Error: top must be between 1 and {MaxTop}.");

        try
        {
            var collector = new MotelyTopSeedSink.Collector(top);
            var details = new Dictionary<string, (int Score, int[] Tallies)>(StringComparer.Ordinal);
            object gate = new();

            // int.MinValue disables the filter's fixed floor: a seed scoring 40 still has a winning
            // score when the filter's cutoff is 100. Auto-cutoff is off for the same reason.
            var settings = MotelySearchBuilder.CreateSettings(loaded!.Config, int.MinValue)
                .WithThreadCount(1)
                .WithQuietMode(true)
                .WithAutoScoreCutoff(false)
                .WithScoredResultGate(null)
                .WithSeedList(listed);

            void Consider(string seed, int score, int[] tallies)
            {
                lock (gate)
                    details.TryAdd(seed, (score, tallies));
                collector.Consider(seed, score);
            }

            settings = settings.SeedScoreDesc is not null
                ? settings.WithScoredResultCallback(result =>
                    Consider(result.Seed, result.Score, result.Tallies.ToArray()))
                : settings.WithSeedMatchCallback(seed => Consider(seed, 0, []));

            using var search = settings.Start();
            search.AwaitCompletion();

            string[] winners = collector.GetSeeds().ToArray();
            var ranked = winners
                .Select(seed => details.TryGetValue(seed, out var detail)
                    ? new { seed, detail.Score, detail.Tallies }
                    : new { seed, Score = 0, Tallies = Array.Empty<int>() })
                .ToArray();

            string header =
                $"best seed for {loaded.Label} from {McpFormat.Number(listed.Length)} listed seeds: " +
                (ranked.Length > 0 ? $"{ranked[0].seed}, score {ranked[0].Score}." : "no matches.");
            string text = ranked.Length == 0
                ? header
                : header + "\nTop:" + string.Join(
                    "\n",
                    ranked.Select((entry, index) => $"  {index + 1}. {entry.seed} — {entry.Score}")
                );

            return ToolResults.Ok(
                text,
                new
                {
                    ok = true,
                    filter = loaded.Label,
                    listed = listed.Length,
                    searched = search.TotalSeedsSearched,
                    matched = search.MatchingSeeds,
                    best = ranked.Length > 0 ? ranked[0] : null,
                    top = ranked,
                }
            );
        }
        catch (Exception ex)
        {
            return ToolResults.Error($"Error: the best-seed check failed: {ex.GetBaseException().Message}");
        }
    }
}
