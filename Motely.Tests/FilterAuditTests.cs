using Motely.Filters;
using Xunit.Abstractions;

namespace Motely.Tests;

public sealed class FilterAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void AuditRootFiltersFolder()
    {
        var root = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var dir = Path.Join(root, "Filters");
        Assert.True(Directory.Exists(dir), $"Filters directory missing: {dir}");

        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jaml", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToArray();

        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var config = JamlConfig.FromFile(file);
                if (config.HasAnyClauses())
                {
                    _ = MotelySearchBuilder.CreatePlan(config);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetRelativePath(dir, file)}: {ex.Message}");
            }
        }

        output.WriteLine($"Total Root Filters: {files.Length}, Failures: {failures.Count}");
        foreach (var f in failures)
        {
            output.WriteLine($"FAIL: {f}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void WhimsyDicetrick_SavedSearchPartyFindsStillMatch()
    {
        var root = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var config = JamlConfig.FromFile(Path.Join(root, "Filters", "WhimsyDicetrick.yaml"));
        var saved = config.Seeds
            .Select(MotelyGlobals.NormalizeSeed)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(saved);

        var found = new HashSet<string>(StringComparer.Ordinal);
        var settings = MotelySearchBuilder.CreateSettings(config, int.MinValue)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithAutoScoreCutoff(false)
            .WithScoredResultGate(null)
            .WithSeedList([.. saved]);
        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(result => { found.Add(result.Seed); })
            : settings.WithSeedMatchCallback(seed => { found.Add(seed); });

        using var search = settings.Start();
        search.AwaitCompletion();

        Assert.Equal(saved.Count, search.MatchingSeeds);
        Assert.Equal(saved, found);
    }
}
