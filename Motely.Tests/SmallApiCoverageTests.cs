using System.ComponentModel;
using Motely.Enums;
using Motely.SeedProviders;

namespace Motely.Tests;

/// <summary>Small public helpers that should stay cheap to exercise and hard to break.</summary>
public sealed class SmallApiCoverageTests
{
    [Fact]
    public void FormatStandardcardRank_CoversEveryAbbreviation()
    {
        for (int rank = 2; rank <= 10; rank++)
            Assert.Equal(rank.ToString(), FormatUtils.FormatStandardcardRank(rank.ToString()));
        Assert.Equal("10", FormatUtils.FormatStandardcardRank("10"));
        Assert.Equal("Jack", FormatUtils.FormatStandardcardRank("J"));
        Assert.Equal("Queen", FormatUtils.FormatStandardcardRank("Q"));
        Assert.Equal("King", FormatUtils.FormatStandardcardRank("K"));
        Assert.Equal("Ace", FormatUtils.FormatStandardcardRank("A"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormatUtils.FormatStandardcardRank("Z"));
    }

    [Fact]
    public void FormatPackName_NamesEveryBoosterPack()
    {
        foreach (MotelyBoosterPack pack in Enum.GetValues<MotelyBoosterPack>())
            Assert.False(string.IsNullOrWhiteSpace(FormatUtils.FormatPackName(pack)));
    }

    [Fact]
    public void FormatStandardcard_NamesValidCardsAndRejectsBadRanks()
    {
        Assert.Equal("Ace of Clubs", FormatUtils.FormatStandardcard(MotelyStandardCard.AceOfClubs));
        var badRank = (MotelyStandardCard)((int)MotelyStandardcardSuit.Clubs | 13);
        Assert.Throws<InvalidEnumArgumentException>(() => FormatUtils.FormatStandardcard(badRank));
    }

    [Fact]
    public void TryParseMotelyItem_AcceptsEnumMemberNames()
    {
        Assert.True(FormatUtils.TryParseMotelyItem("Mercury", out var item));
        Assert.Equal(MotelyItemType.Mercury, item.Type);
        // PlanetX's display name is "Planet X", so the bare member name takes the enum fallback.
        Assert.True(FormatUtils.TryParseMotelyItem("PlanetX", out item));
        Assert.Equal(MotelyItemType.PlanetX, item.Type);
        Assert.False(FormatUtils.TryParseMotelyItem("NotAnItem", out _));
        Assert.False(FormatUtils.TryParseMotelyItem("Eternal ", out _));
    }

    [Fact]
    public void MotelySingleItemSet_AppendsIndexesContainsAndPrints()
    {
        var mercury = new MotelyItem(MotelyItemType.Mercury);
        var venus = new MotelyItem(MotelyItemType.Venus);
        var set = new MotelySingleItemSet(mercury, venus);

        Assert.Equal(2, set.Length);
        Assert.Equal(mercury, set[0]);
        Assert.Equal(venus, set.GetItem(1));
        Assert.True(set.Contains(MotelyItemType.Mercury));
        Assert.True(set.Contains(venus));
        Assert.False(set.Contains(MotelyItemType.Earth));

        set.Append(new MotelyItem(MotelyItemType.Earth));
        set[0] = venus;
        Assert.Equal(3, set.Length);
        Assert.Equal(venus, set[0]);
        Assert.Equal(3, set.AsArray().Length);
        Assert.Contains("Venus", set.ToString());
    }

    [Fact]
    public void EveryAesthetic_CountsAndEnumeratesSeeds()
    {
        foreach (MotelyAesthetic aesthetic in Enum.GetValues<MotelyAesthetic>())
        {
            Assert.True(
                MotelyAesthetics.GetSeedCount(aesthetic, MotelyAesthetics.QuickPaddingChars) > 0,
                $"{aesthetic} should enumerate seeds"
            );
            using var seeds = MotelyAesthetics.EnumerateSeeds(aesthetic, MotelyAesthetics.QuickPaddingChars)
                .GetEnumerator();
            Assert.True(seeds.MoveNext(), $"{aesthetic} should yield a seed");
            Assert.False(string.IsNullOrWhiteSpace(seeds.Current));
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MotelyAesthetics.GetSeedCount((MotelyAesthetic)999)
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MotelyAesthetics.EnumerateSeeds((MotelyAesthetic)999).First()
        );
    }
}
