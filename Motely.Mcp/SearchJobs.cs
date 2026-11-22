using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Motely.Filters;

namespace Motely.Mcp;

/// <summary>What <c>search</c> takes besides the filter and the mode. Every field is optional.</summary>
public sealed record SearchOptions
{
    /// <summary>random: how many seeds to draw (default 1,000,000).</summary>
    public int? Count { get; init; }

    /// <summary>keyword: the word, or comma-separated words, a seed must contain.</summary>
    public string? Keyword { get; init; }

    /// <summary>keyword / aesthetic: the characters that fill the other slots (default 1-9 and A-Z).</summary>
    public string? Padding { get; init; }

    /// <summary>aesthetic: the family name, or "all" (default).</summary>
    public string? Aesthetic { get; init; }

    /// <summary>seed_list: the seeds to search.</summary>
    public string[]? Seeds { get; init; }

    /// <summary>sequential: start at the batch holding this 8-character seed.</summary>
    public string? StartSeed { get; init; }

    /// <summary>sequential: end after the batch holding this 8-character seed.</summary>
    public string? StopSeed { get; init; }

    /// <summary>Worker threads (default: cores minus 2).</summary>
    public int? Threads { get; init; }

    /// <summary>Stop after N matches (each thread finishes its batch, so a few more can arrive).</summary>
    public long? Collect { get; init; }

    /// <summary>Report only seeds scoring at least this. Default: the filter's own cutoff, else none.</summary>
    public int? MinScore { get; init; }

    /// <summary>Write the top matches into the filter file's seeds: block when the search ends. Needs filterPath.</summary>
    public bool Save { get; init; }

    /// <summary>The filter file to save into when save is true.</summary>
    public string? FilterPath { get; init; }
}

public sealed record SearchMatch(string Seed, int Score, int[] Tallies);

/// <summary>One running or finished search. Holds the best matches in memory; nothing goes to disk unless save is set.</summary>
public sealed class SearchJob
{
    /// <summary>Matches kept per job, best score first. Beyond it the lowest scores drop out.</summary>
    public const int KeepMatches = 10_000;

    private readonly object gate = new();
    private readonly List<SearchMatch> matches = [];
    private readonly ConcurrentDictionary<string, SeedVerdict> rechecked = new();
    private readonly CancellationTokenSource cts = new();
    private long matchCount;
    private MotelyProgress? progress;

    public required string Id { get; init; }
    public required string Mode { get; init; }
    public required JamlConfig Config { get; init; }
    public required IReadOnlyList<string> TallyLabels { get; init; }
    public required int Threads { get; init; }
    public required SearchOptions Options { get; init; }
    public IMotelySearch Search { get; private set; } = null!;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public string State { get; private set; } = "running";
    public string? Error { get; private set; }
    public string? SaveResult { get; private set; }

    public long MatchCount => Interlocked.Read(ref matchCount);

    internal CancellationToken Token => cts.Token;

    internal void Add(string seed, int score, int[] tallies)
    {
        Interlocked.Increment(ref matchCount);
        lock (gate)
        {
            matches.Add(new SearchMatch(seed, score, tallies));
            if (matches.Count >= KeepMatches * 2)
            {
                SortLocked();
                matches.RemoveRange(KeepMatches, matches.Count - KeepMatches);
            }
        }
    }

    internal void Report(MotelyProgress p) => progress = p;

    private void SortLocked() =>
        matches.Sort(static (a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Seed, b.Seed));

    /// <summary>The best matches so far, best score first (ties by seed).</summary>
    public List<SearchMatch> Top(int n)
    {
        lock (gate)
        {
            SortLocked();
            if (matches.Count > KeepMatches)
                matches.RemoveRange(KeepMatches, matches.Count - KeepMatches);
            return [.. matches.Take(n)];
        }
    }

    /// <summary>
    /// The debug check: each match is scored again, clause by clause, on the scalar path
    /// (<see cref="McpEngine.ScoreSeeds"/>), which does not share the search's SIMD chain.
    /// A match that fails a must there, or scores differently, comes back as a mismatch.
    /// </summary>
    public List<(SearchMatch Match, SeedVerdict Verdict)> Recheck(IReadOnlyList<SearchMatch> list)
    {
        var missing = list.Select(m => m.Seed).Where(s => !rechecked.ContainsKey(s)).Distinct().ToArray();
        if (missing.Length > 0)
            foreach (var v in McpEngine.ScoreSeeds(Config, missing))
                rechecked[v.Seed] = v;
        return [.. list.Select(m => (m, rechecked[m.Seed]))];
    }

    public static bool Agrees(SearchMatch match, SeedVerdict verdict) =>
        verdict.Pass
        && verdict.Score == match.Score
        && (match.Tallies.Length == 0 || match.Tallies.SequenceEqual(verdict.Tallies!.Select(t => t.Count)));

    internal void Begin(IMotelySearch search)
    {
        Search = search;
        Completion = Task.Run(async () =>
        {
            try
            {
                await search.WaitForCompletionAsync();
                State = cts.IsCancellationRequested ? "cancelled"
                    : search.StoppedOnMatchLimit ? "stopped at collect"
                    : "finished";
                if (Options.Save)
                    SaveSeeds();
            }
            catch (Exception ex)
            {
                State = "failed";
                Error = ex.Message;
            }
        });
    }

    public void Cancel() => cts.Cancel();

    /// <summary>
    /// Writes the top matches into the filter file's seeds: block, only when the call set save:
    /// true. Only matches that pass the recheck are written.
    /// </summary>
    private void SaveSeeds()
    {
        if (string.IsNullOrWhiteSpace(Options.FilterPath))
        {
            SaveResult = "not saved: save needs filterPath";
            return;
        }
        var top = Top(2000);
        var good = Recheck(top).Where(r => Agrees(r.Match, r.Verdict)).Select(r => r.Match.Seed).ToList();
        try
        {
            var text = File.ReadAllText(Options.FilterPath);
            if (MotelyTopSeedSink.TryRewriteAndValidate(text, good, out var updated, out var error))
            {
                File.WriteAllText(Options.FilterPath, updated);
                SaveResult = $"saved {good.Count} seed(s) into {Options.FilterPath}";
            }
            else
                SaveResult = $"not saved: {error}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveResult = $"not saved: {ex.Message}";
        }
    }

    public JsonObject Status(int top = 50)
    {
        var search = Search;
        bool done = Completion.IsCompleted;
        long searched = done ? search.TotalSeedsSearched : progress?.SeedsSearched ?? search.TotalSeedsSearched;
        double speed = done ? search.SeedsPerSecond : (progress?.SeedsPerMillisecond ?? 0) * 1000.0;
        long elapsed = done ? search.ElapsedMs : progress?.ElapsedMilliseconds ?? (long)(DateTime.UtcNow - StartedUtc).TotalMilliseconds;

        var list = Top(top);
        var checkedList = Recheck(list);
        var rows = new JsonArray();
        var mismatches = new JsonArray();
        foreach (var (match, verdict) in checkedList)
        {
            bool ok = Agrees(match, verdict);
            var tallies = new JsonObject();
            for (int i = 0; i < match.Tallies.Length; i++)
                tallies[i < TallyLabels.Count ? TallyLabels[i] : $"score{i}"] = match.Tallies[i];
            rows.Add(new JsonObject
            {
                ["seed"] = match.Seed,
                ["score"] = match.Score,
                ["tallies"] = tallies,
                ["recheck"] = ok ? "pass" : "MISMATCH",
            });
            if (!ok)
                mismatches.Add(System.Text.Json.JsonSerializer.SerializeToNode(verdict, McpEngine.Json));
        }

        var status = new JsonObject
        {
            ["jobId"] = Id,
            ["state"] = State,
            ["mode"] = Mode,
            ["deck"] = Config.Deck.ToString(),
            ["stake"] = Config.Stake.ToString(),
            ["threads"] = Threads,
            ["seedsSearched"] = searched,
            ["seedsPerSecond"] = Math.Round(speed),
            ["elapsedMs"] = elapsed,
            ["matches"] = MatchCount,
            ["top"] = rows,
            ["recheck"] = mismatches.Count == 0
                ? $"all {rows.Count} matches shown pass every must clause on the scalar recheck"
                : $"{mismatches.Count} of {rows.Count} matches shown FAILED the scalar recheck",
        };
        if (progress?.PercentComplete is > 0 and var pct && !done)
            status["percentComplete"] = Math.Round(pct, 4);
        if (search.IsSequentialBatchSearch && done && search.ResumeBatchIndex >= 0)
            status["resumeBatch"] = search.ResumeBatchIndex;
        if (mismatches.Count > 0)
            status["mismatches"] = mismatches;
        if (Error is not null)
            status["error"] = Error;
        if (SaveResult is not null)
            status["save"] = SaveResult;
        return status;
    }
}

/// <summary>Starts searches and keeps them by id for search_status and search_cancel.</summary>
public static class SearchJobs
{
    public static readonly string[] Modes = ["sequential", "random", "keyword", "aesthetic", "seed_list"];

    private static readonly ConcurrentDictionary<string, SearchJob> jobs = new();
    private static int next;

    public static SearchJob? Get(string id) => jobs.TryGetValue(id?.Trim() ?? "", out var job) ? job : null;

    public static SearchJob Start(JamlConfig config, string mode, SearchOptions? options)
    {
        options ??= new SearchOptions();
        mode = (mode ?? "sequential").Trim().ToLowerInvariant();
        if (!Modes.Contains(mode))
            throw new ArgumentException($"unknown mode '{mode}'; known: {string.Join(", ", Modes)}.");
        if (options.Save && string.IsNullOrWhiteSpace(options.FilterPath))
            throw new ArgumentException("save: true needs filterPath, the filter file to write the seeds: block into.");
        if (options.Collect is < 0)
            throw new ArgumentException("collect needs a count of 0 or more.");

        int threads = Math.Max(1, options.Threads ?? McpEngine.DefaultThreads);
        var plan = MotelySearchBuilder.CreatePlan(config, options.MinScore);
        var settings = plan.Settings.WithThreadCount(threads).WithAutoScoreCutoff(false);

        var keywords = mode == "keyword"
            ? (options.Keyword ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        if (mode == "keyword" && keywords.Length == 0)
            throw new ArgumentException("mode keyword needs options.keyword.");
        if (mode == "seed_list" && options.Seeds is not { Length: > 0 })
            throw new ArgumentException("mode seed_list needs options.seeds.");

        var input = new CliSearchMode.Input(
            SeedsArgument: mode == "seed_list" ? string.Join(",", options.Seeds!) : null,
            Replay: false,
            JamlPath: null,
            JamlSeeds: null,
            KeywordInputs: keywords,
            PaddingCharsOption: mode is "keyword" or "aesthetic" ? options.Padding : null,
            RandomCount: mode == "random" ? options.Count ?? 1_000_000 : null,
            AestheticName: mode == "aesthetic" ? options.Aesthetic ?? "all" : null,
            StartBatch: null,
            EndBatch: null,
            StartPercent: null,
            StartSeed: mode == "sequential" ? options.StartSeed : null,
            StopSeed: mode == "sequential" ? options.StopSeed : null,
            BatchCharacterCount: null,
            AllowSeedFile: false
        );
        if (!CliSearchMode.TryApplySearchMode(settings, input, out var modeError, out settings))
            throw new ArgumentException(modeError.StartsWith("Error: ") ? modeError[7..] : modeError);

        var job = new SearchJob
        {
            Id = $"job{Interlocked.Increment(ref next)}",
            Mode = mode,
            Config = config,
            TallyLabels = plan.TallyLabels,
            Threads = threads,
            Options = options,
        };

        // Exactly one channel, as the CLI listens: scored when there is a score provider.
        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(r => job.Add(r.Seed, r.Score, r.Tallies))
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                job.Add(comma < 0 ? line : line[..comma], 0, []);
            });
        settings = settings.WithProgressCallback(job.Report);
        if (options.Collect is { } collect)
            settings = settings.StopAfter(collect);

        jobs[job.Id] = job;
        job.Begin(settings.Start(job.Token));
        return job;
    }
}
