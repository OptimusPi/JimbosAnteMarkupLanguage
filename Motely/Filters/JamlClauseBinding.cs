using System.Collections.Frozen;
namespace Motely.Filters;

/// <summary>JAML clause discriminators and scalar-or-list converters.</summary>
internal static class JamlClauseBinding
{
    internal static IReadOnlyCollection<string> ClauseKeys => DiscriminatorKeys;

    internal static readonly Dictionary<string, Type> ClauseTypeByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["joker"] = typeof(JokerClause),
        ["jokers"] = typeof(JokerClause),
        ["commonJoker"] = typeof(CommonJokerClause),
        ["commonJokers"] = typeof(CommonJokerClause),
        ["uncommonJoker"] = typeof(UncommonJokerClause),
        ["uncommonJokers"] = typeof(UncommonJokerClause),
        ["rareJoker"] = typeof(RareJokerClause),
        ["rareJokers"] = typeof(RareJokerClause),
        ["legendaryJoker"] = typeof(LegendaryJokerClause),
        ["legendaryJokers"] = typeof(LegendaryJokerClause),
        ["soulJoker"] = typeof(LegendaryJokerClause),
        ["planetCard"] = typeof(PlanetCardClause),
        ["planetCards"] = typeof(PlanetCardClause),
        ["spectralCard"] = typeof(SpectralCardClause),
        ["spectralCards"] = typeof(SpectralCardClause),
        ["tarotCard"] = typeof(TarotCardClause),
        ["tarotCards"] = typeof(TarotCardClause),
        ["standardCard"] = typeof(StandardCardClause),
        ["standardCards"] = typeof(StandardCardClause),
        ["erraticRank"] = typeof(ErraticRankClause),
        ["erraticRanks"] = typeof(ErraticRankClause),
        ["erraticSuit"] = typeof(ErraticSuitClause),
        ["erraticSuits"] = typeof(ErraticSuitClause),
        ["voucher"] = typeof(VoucherClause),
        ["vouchers"] = typeof(VoucherClause),
        ["tag"] = typeof(TagClause),
        ["tags"] = typeof(TagClause),
        ["smallBlindTag"] = typeof(SmallBlindTagClause),
        ["bigBlindTag"] = typeof(BigBlindTagClause),
        ["boss"] = typeof(BossClause),
        ["bosses"] = typeof(BossClause),
        ["boosterPack"] = typeof(BoosterPackClause),
        ["boosterPacks"] = typeof(BoosterPackClause),
        ["pokerHand"] = typeof(PokerHandClause),
        ["pokerHands"] = typeof(PokerHandClause),
        ["startingDraw"] = typeof(StartingDrawClause),
        ["luckyMoney"] = typeof(LuckyMoneyClause),
        ["luckyMult"] = typeof(LuckyMultClause),
        ["misprintMult"] = typeof(MisprintMultClause),
        ["wheelOfFortune"] = typeof(WheelOfFortuneClause),
        ["wheelStaysFlipped"] = typeof(WheelStaysFlippedClause),
        ["glassDestroy"] = typeof(GlassDestroyClause),
        ["cavendishExtinct"] = typeof(CavendishExtinctClause),
        ["grosMichelExtinct"] = typeof(GrosMichelExtinctClause),
        ["spaceLevelup"] = typeof(SpaceLevelupClause),
        ["businessPayout"] = typeof(BusinessPayoutClause),
        ["parkingPayout"] = typeof(ParkingPayoutClause),
        ["bloodstoneTrigger"] = typeof(BloodstoneTriggerClause),
        ["and"] = typeof(AndClause),
        ["or"] = typeof(OrClause),
        ["soulOrder"] = typeof(LegendarySoulOrderClause),
        ["soulOrders"] = typeof(LegendarySoulOrderClause),
    };

    private static readonly FrozenSet<string> DiscriminatorKeys =
        ClauseTypeByKey.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The words that mean "any" where a name, a card or an event's rolls is written, in any case.
    /// An empty value means the same.
    /// </summary>
    internal static readonly string[] AnyWords = ["any", "~", "null"];

    internal static bool IsAnyWord(string value)
    {
        var text = value.AsSpan().Trim();
        if (text.Length == 0)
            return true;
        foreach (var word in AnyWords)
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
