using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// The input guards are the bot/CLI UX: every bad combination must come back as a short Error:,
/// never a stack trace. These fill in the search-mode paths the replay tests don't take.
/// </summary>
public sealed class CliSearchModeMoreTests
{
    private static IMotelySearchSettings BaseSettings() =>
        MotelySearchBuilder.CreateSettings(JamlConfig.FromJaml("seeds: []"));

    private static CliSearchMode.Input EmptyInput() =>
        new(
            SeedsArgument: null,
            Replay: false,
            JamlPath: null,
            JamlSeeds: null,
            KeywordInputs: [],
            PaddingCharsOption: null,
            RandomCount: null,
            AestheticName: null,
            StartBatch: null,
            EndBatch: null,
            StartPercent: null,
            StartSeed: null,
            StopSeed: null,
            BatchCharacterCount: null
        );

    private static void AssertOk(CliSearchMode.Input input, Action<IMotelySearchSettings>? check = null)
    {
        bool ok = CliSearchMode.TryApplySearchMode(BaseSettings(), input, out string? error, out var updated);
        Assert.True(ok, error);
        Assert.Null(error);
        check?.Invoke(updated);
    }

    private static void AssertError(CliSearchMode.Input input, string expected)
    {
        bool ok = CliSearchMode.TryApplySearchMode(BaseSettings(), input, out string? error, out _);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData(" a1b2c3d4 ", "A1B2C3D4")]
    [InlineData("00000000", "OOOOOOOO")]
    [InlineData("", "")]
    public void TryNormalizeSeed_AcceptsGameSeedSpellings(string raw, string expected)
    {
        Assert.True(CliSearchMode.TryNormalizeSeed(raw, out string? seed));
        Assert.Equal(expected, seed);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("ABCDEFGHI")]
    [InlineData("ABC-123")]
    public void TryNormalizeSeed_RefusesNonSeeds(string raw)
    {
        Assert.False(CliSearchMode.TryNormalizeSeed(raw, out string? seed));
        Assert.Null(seed);
    }

    [Fact]
    public void SeedsArgument_InstallsTheListProvider()
    {
        AssertOk(EmptyInput() with { SeedsArgument = "11111111,22222222" }, updated =>
            Assert.NotNull(((MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated).SeedProvider)
        );
    }

    [Fact]
    public void Replay_UsesTheJamlsOwnSeeds()
    {
        AssertOk(
            EmptyInput() with { Replay = true, JamlPath = "f.yaml", JamlSeeds = ["11111111", "22222222"] },
            updated =>
                Assert.NotNull(((MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated).SeedProvider)
        );
    }

    [Fact]
    public void Random_AndAesthetic_InstallTheirProviders()
    {
        AssertOk(EmptyInput() with { RandomCount = 100 }, updated =>
            Assert.NotNull(((MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated).SeedProvider)
        );
        AssertOk(EmptyInput() with { AestheticName = "mirror" }, updated =>
            Assert.NotNull(((MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated).SeedProvider)
        );
        AssertOk(EmptyInput() with { AestheticName = "all" }, updated =>
            Assert.NotNull(((MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated).SeedProvider)
        );
    }

    [Fact]
    public void UnknownAesthetic_GivesAKnownValuesHint()
    {
        AssertError(
            EmptyInput() with { AestheticName = "nonsense" },
            "Error: unknown --aesthetic value 'nonsense'. Known:"
        );
    }

    [Fact]
    public void TwoSearchModes_RefusesWithTheModeList()
    {
        AssertError(
            EmptyInput() with { SeedsArgument = "11111111", RandomCount = 10 },
            "Error: choose only one search input mode:"
        );
    }

    [Fact]
    public void ExternalSeeds_AreAcceptedWithoutInstallingAProvider()
    {
        AssertOk(EmptyInput() with { HasExternalSeeds = true });
    }

    [Fact]
    public void Replay_WithoutJamlOrSeeds_RefusesCleanly()
    {
        AssertError(EmptyInput() with { Replay = true }, "Error: --replay requires --jaml");
        AssertError(
            EmptyInput() with { Replay = true, JamlPath = "f.yaml" },
            "Error: 'f.yaml' has no seeds: block to replay."
        );
    }

    [Fact]
    public void RandomCount_Negative_RefusesCleanly()
    {
        AssertError(EmptyInput() with { RandomCount = -1 }, "Error: --random needs a count of 0 or more.");
    }

    [Fact]
    public void BadBatchCharacterCount_RefusesCleanly()
    {
        AssertError(
            EmptyInput() with { BatchCharacterCount = 8 },
            "Error: --batchCharCount must be 1-7."
        );
    }

    [Fact]
    public void StartAndStopSeeds_UseSweepOrderNotAlphabetical()
    {
        AssertOk(EmptyInput() with { StartSeed = "11111111", StopSeed = "22222222" }, updated =>
        {
            var typed = (MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated;
            Assert.True(typed.StartBatchIndex >= 0);
            Assert.True(typed.EndBatchIndex > typed.StartBatchIndex);
        });
        AssertError(
            EmptyInput() with { StartSeed = "22222222", StopSeed = "11111111" },
            "Error: --stopSeed"
        );
    }

    [Fact]
    public void BadSeedBounds_RefuseCleanly()
    {
        AssertError(
            EmptyInput() with { StartSeed = "ABC" },
            "Error: --startSeed 'ABC' is not an 8-character seed"
        );
        AssertError(
            EmptyInput() with { StopSeed = "ABC" },
            "Error: --stopSeed 'ABC' is not an 8-character seed"
        );
        AssertError(
            EmptyInput() with { StartSeed = "11111111", StartBatch = 0 },
            "Error: do not combine --startSeed/--stopSeed"
        );
    }

    [Fact]
    public void StartBatch_AndStartPercent_AreBounded()
    {
        AssertError(
            EmptyInput() with { StartBatch = -1 },
            "Error: --startBatch must be 0.."
        );
        AssertError(
            EmptyInput() with { StartPercent = 100.1 },
            "Error: --startPercent must be between 0 and 100."
        );
        AssertOk(EmptyInput() with { StartPercent = 50 }, updated =>
        {
            var typed = (MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>)updated;
            Assert.True(typed.StartBatchIndex >= 0);
        });
    }

    [Fact]
    public void EndBatch_MustFollowTheStart()
    {
        AssertError(
            EmptyInput() with { StartBatch = 2, EndBatch = 2 },
            "Error: --endBatch 2 must be greater than the start batch 2."
        );
        AssertOk(EmptyInput() with { StartBatch = 2, EndBatch = 3 });
    }

    [Fact]
    public void InvalidPaddingAndKeyword_RefuseCleanly()
    {
        AssertError(
            EmptyInput() with { KeywordInputs = ["COLA"], PaddingCharsOption = "!!!" },
            "Error: --padding '!!!' has no seed characters"
        );
        AssertError(
            EmptyInput() with { KeywordInputs = ["!!!"] },
            "Error: keyword '!!!' is not part of a seed"
        );
    }

    [Fact]
    public void MissingSeedFile_GivesAPathError()
    {
        Assert.False(CliSearchMode.TryResolveSeeds("definitely-not-a-seed-file.txt", out _, out var error));
        Assert.Contains("no seed file at", error);
        Assert.False(CliSearchMode.TryApplySearchMode(
            BaseSettings(),
            EmptyInput() with { SeedsArgument = "definitely-not-a-seed-file.txt" },
            out error,
            out _
        ));
        Assert.Contains("no seed file at", error);
    }

    [Fact]
    public void SeedFileWithNoUsableSeeds_RefusesCleanly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"motely-empty-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path, ["seed,score", "", "not a seed,0"]);
            Assert.False(CliSearchMode.TryResolveSeeds(path, out _, out var error));
            Assert.Contains("no seeds in", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StartPercentAtTheEnd_ClampsToTheLastBatch()
    {
        AssertOk(EmptyInput() with { StartPercent = 100 });
    }

    [Fact]
    public void InlineHelpers_HandleEmptyAndWhitespaceEntries()
    {
        Assert.Null(CliSearchMode.Padding(null));
        Assert.NotNull(CliSearchMode.Padding("123"));

        Assert.True(CliSearchMode.TryResolveInlineSeeds("", out var empty, out var error), error);
        Assert.Equal([""], empty);

        Assert.True(CliSearchMode.TryResolveSeeds("11111111, ,22222222", out var seeds, out error), error);
        Assert.Equal(["11111111", "22222222"], seeds);
    }

    [Fact]
    public void ResolveSeeds_AcceptsInlineFilesAndRejectsServerSidePaths()
    {
        Assert.True(CliSearchMode.TryResolveSeeds("a1b2c3d4,22222222", out var inline, out var error), error);
        Assert.Equal(["A1B2C3D4", "22222222"], inline);
        Assert.Null(error);

        Assert.True(CliSearchMode.TryResolveSeeds("", out var empty, out error), error);
        Assert.Equal([""], empty);

        Assert.False(CliSearchMode.TryResolveSeeds("   ", out _, out error));
        Assert.Contains("no seeds in", error);
        Assert.False(CliSearchMode.TryResolveSeeds("ABC-123", out _, out error));
        Assert.Contains("is not a seed", error);

        Assert.False(CliSearchMode.TryResolveInlineSeeds("-", out _, out error));
        Assert.Contains("stdin) is not allowed", error);
        Assert.False(CliSearchMode.TryResolveInlineSeeds(@"C:\nope\seeds.csv", out _, out error));
        Assert.Contains("file paths are not allowed", error);

        string path = Path.Combine(Path.GetTempPath(), $"motely-seeds-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(path, ["seed,score", "11111111,10", "", "=22222222,20", "not a seed,0"]);
            Assert.True(CliSearchMode.TryResolveSeeds(path, out var fromFile, out error), error);
            Assert.Equal(["11111111", "22222222"], fromFile);
            Assert.Null(error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
