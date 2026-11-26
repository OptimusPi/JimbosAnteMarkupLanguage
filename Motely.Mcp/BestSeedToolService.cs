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
            // Keep the rank and its tallies in one bounded heap. A dictionary of every match used
            // to grow with the pile even though top caps the answer at 100 rows.
            var rankedHeap = new PriorityQueue<RankedSeed, (int Score, long Sequence)>(
                Comparer<(int Score, long Sequence)>.Create(static (a, b) =>
                {
                    int byScore = a.Score.CompareTo(b.Score);
                    return byScore != 0 ? byScore : b.Sequence.CompareTo(a.Sequence);
                })
            );
            long sequence = 0;
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
                {
                    rankedHeap.Enqueue(
                        new RankedSeed(seed, score, tallies, sequence),
                        (score, sequence)
                    );
                    sequence++;
                    if (rankedHeap.Count > top)
                        rankedHeap.Dequeue();
                }
            }

            // A filter without `should:` clauses scores every seed 0. Say so, instead of presenting
            // the first listed seed as a meaningful winner. (The engine still installs a score
            // provider, so the flag comes from the filter, not from SeedScoreDesc.)
            bool scored = loaded.Config.Should.Count > 0;
            settings = settings.SeedScoreDesc is not null
                ? settings.WithScoredResultCallback(result =>
                    Consider(result.Seed, result.Score, result.Tallies.ToArray()))
                : settings.WithSeedMatchCallback(seed => Consider(seed, 0, []));

            using var search = settings.Start();
            search.AwaitCompletion();

            var ranked = rankedHeap
                .UnorderedItems
                .Select(entry => entry.Element)
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Sequence)
                .Select(entry => new { seed = entry.Seed, entry.Score, entry.Tallies })
                .ToArray();

            string header =
                $"best seed for {loaded.Label} from {McpFormat.Number(listed.Length)} listed seeds: " +
                (ranked.Length > 0 ? $"{ranked[0].seed}, score {ranked[0].Score}." : "no matches.");
            if (!scored)
                header +=
                    " This filter has no `should:` clauses, so every seed scores 0; "
                    + "the first listed seed is the winner.";
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
                    scored,
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

    private sealed record RankedSeed(string Seed, int Score, int[] Tallies, long Sequence);
}
