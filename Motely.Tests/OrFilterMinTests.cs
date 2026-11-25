using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// `or: { min: 2 }` takes the counted-lane vector path in OrFilterDesc, not the standard OR fast
/// path. Keep that path exercised: it must still agree with the scalar score.
/// </summary>
public sealed class OrFilterMinTests
{
    [Fact]
    public void OrWithMinTwo_RequiresBothArmsOnTheSameLane()
    {
        const string jaml = """
            name: or-min-two
            deck: Red
            stake: White
            must:
              - or:
                  - boss: Any
                    antes: [1]
                  - boss: Any
                    antes: [1]
                min: 2
            seeds: [C3DW94B8]
            """;
        var config = JamlConfig.FromJaml(jaml);
        var matches = new List<string>();

        using var search = MotelySearchBuilder
            .CreateSettings(config)
            .WithSeedList([.. config.Seeds])
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(matches.Add)
            .Start();
        search.AwaitCompletion();

        Assert.Equal(1, search.MatchingSeeds);
        Assert.Equal("C3DW94B8", Assert.Single(matches));
    }
}
