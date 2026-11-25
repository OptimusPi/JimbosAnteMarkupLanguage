using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters;

public static partial class ClauseScoring
{
    private static int CountJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        JokerClause clause,
        MotelyRunState runState
    )
    {
        return CountJokerClauseOccurrences(ref ctx, clause, runState);
    }

    private static int CountCommonJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        CommonJokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? CommonJokerFilterDesc.DefaultSources;
        if ((clause.Jokers.Length == 0))
            return CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                MotelyJokerRarity.Common,
                clause.Edition,
                clause.Stickers,
                runState,
                clause.With
            );
        return CountJokerOccurrencesGeneric(
            ref ctx,
            clause.Antes,
            sources,
            clause.Jokers,
            clause.Edition,
            clause.Stickers,
            runState,
            clause.With
        );
    }

    private static int CountUncommonJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        UncommonJokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? UncommonJokerFilterDesc.DefaultSources;
        if ((clause.Jokers.Length == 0))
            return CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                MotelyJokerRarity.Uncommon,
                clause.Edition,
                clause.Stickers,
                runState,
                clause.With
            );
        return CountJokerOccurrencesGeneric(
            ref ctx,
            clause.Antes,
            sources,
            clause.Jokers,
            clause.Edition,
            clause.Stickers,
            runState,
            clause.With
        );
    }

    private static int CountRareJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        RareJokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? RareJokerFilterDesc.DefaultSources;
        if ((clause.Jokers.Length == 0))
            return CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                MotelyJokerRarity.Rare,
                clause.Edition,
                clause.Stickers,
                runState,
                clause.With
            );
        return CountJokerOccurrencesGeneric(
            ref ctx,
            clause.Antes,
            sources,
            clause.Jokers,
            clause.Edition,
            clause.Stickers,
            runState,
            clause.With
        );
    }

    /// <summary>
    /// Scalar joker count for the SIMD filter's per-seed confirmation pass.
    /// Same prepare + count as <see cref="ClauseMeetsMinForFilter"/> / should-scoring.
    /// </summary>
    internal static int CountJokerClauseOccurrencesForFilter(
        ref MotelySingleSearchContext ctx,
        JokerClause clause
    )
    {
        var runState = ctx.Deck.GetDefaultRunState();
        ApplyPrepareRunState(ref ctx, runState, GetMaxAnte(clause), GetMaxBossAnte(clause));
        return CountJokerClauseOccurrences(ref ctx, clause, runState);
    }

    private static int CountJokerClauseOccurrences(
        ref MotelySingleSearchContext ctx,
        JokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? JokerFilterDesc.DefaultSources;
        if ((clause.Jokers.Length == 0))
        {
            int normalWildcard = CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                wildcardRarity: null,
                clause.Edition,
                clause.Stickers,
                runState,
                clause.With
            );

            var wildcardLegendarySources = LegendarySourcesFor(clause);
            if (wildcardLegendarySources is null)
                return normalWildcard;

            var legendaryWildcard = new LegendaryJokerClause
            {
                Label = clause.Label,
                Score = clause.Score,
                Jokers = [],
                Edition = clause.Edition,
                Sources = wildcardLegendarySources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };

            return normalWildcard
                + CountLegendaryJokerOccurrences(ref ctx, legendaryWildcard, runState);
        }

        var (nonLegendaryTypes, legendary) = SplitByRarity(clause.Jokers);

        int count = 0;

        if (nonLegendaryTypes.Length > 0)
        {
            count += CountJokerOccurrencesOfTypes(
                ref ctx,
                clause.Antes,
                sources,
                nonLegendaryTypes,
                clause.Edition,
                clause.Stickers,
                runState,
                clause.With
            );
        }

        if (legendary.Length > 0 && LegendarySourcesFor(clause) is { } legendarySources)
        {
            var legendaryClause = new LegendaryJokerClause
            {
                Label = clause.Label,
                Score = clause.Score,
                Jokers = legendary,
                Edition = clause.Edition,
                Sources = legendarySources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };

            count += CountLegendaryJokerOccurrences(ref ctx, legendaryClause, runState);
        }

        return count;
    }

    /// <summary>
    /// Where a <c>joker:</c> clause looks for legendaries (Soul cards in Arcana/Spectral packs).
    /// <c>legendarySources:</c> wins when given. With no <c>sources:</c> block at all, the legendary
    /// default applies. An explicit <c>sources:</c> block is taken literally: its <c>boosterPacks</c>
    /// slots are the Soul slots, and a block that lists no packs asks for no Souls (null) — a clause
    /// never pulls from a source it did not name.
    /// </summary>
    private static LegendaryJokerSourceConfig? LegendarySourcesFor(JokerClause clause)
    {
        if (clause.LegendarySources is { } explicitLegendary)
            return explicitLegendary;
        if (clause.Sources is null)
            return LegendaryJokerFilterDesc.DefaultSources;
        if (clause.Sources.BoosterPacks.Length == 0)
            return null;
        return new LegendaryJokerSourceConfig
        {
            BoosterPacks = clause.Sources.BoosterPacks,
            RequireMegaPack = clause.Sources.RequireMegaPack,
        };
    }

    /// <summary>
    /// A clause's joker list as item types, mapped once per array rather than once per seed
    /// (the mapping goes through the enum names). Keyed on the clause's own array instance, so a
    /// host that assigns a new <c>Jokers</c> array gets a fresh mapping.
    /// </summary>
    private static class JokerItemTypes<TJoker>
        where TJoker : struct, Enum
    {
        private static readonly ConditionalWeakTable<TJoker[], MotelyItemType[]> Cache = new();

        public static MotelyItemType[] Of(TJoker[] jokers) => Cache.GetValue(jokers, Map);

        private static MotelyItemType[] Map(TJoker[] jokers)
        {
            var types = new MotelyItemType[jokers.Length];
            for (int i = 0; i < jokers.Length; i++)
                types[i] = Enum.Parse<MotelyItemType>(jokers[i].ToString(), true);
            return types;
        }
    }

    /// <summary>A <see cref="JokerClause"/>'s list split by rarity, once per array: legendaries are
    /// counted through the soul path, the rest through shop and buffoon packs.</summary>
    private sealed record JokerRaritySplit(MotelyItemType[] NonLegendaryTypes, MotelyJoker[] Legendary);

    private static readonly ConditionalWeakTable<MotelyJoker[], JokerRaritySplit> JokerSplits = new();

    private static JokerRaritySplit SplitByRarity(MotelyJoker[] jokers) =>
        JokerSplits.GetValue(
            jokers,
            static all =>
            {
                static bool IsLegendary(MotelyJoker j) =>
                    ((MotelyJokerRarity)((int)j & MotelyGlobals.JokerRarityMask))
                    == MotelyJokerRarity.Legendary;

                MotelyJoker[] nonLegendary = Array.FindAll(all, static j => !IsLegendary(j));
                return new(
                    JokerItemTypes<MotelyJoker>.Of(nonLegendary),
                    Array.FindAll(all, IsLegendary)
                );
            }
        );

    private static int CountJokerOccurrencesGeneric<TJoker>(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        TJoker[] jokers,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState,
        MotelyWith held
    )
        where TJoker : struct, Enum =>
        CountJokerOccurrencesOfTypes(
            ref ctx,
            antes,
            sources,
            JokerItemTypes<TJoker>.Of(jokers),
            edition,
            stickers,
            runState,
            held
        );

    private static int CountJokerOccurrencesOfTypes(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        MotelyItemType[] targetTypes,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState,
        MotelyWith held
    )
    {
        int count = 0;
        var shopItems = sources.ShopItems;
        var boosterPacks = sources.BoosterPacks;

        int maxShop = ArrayMax(shopItems);
        int userMaxPack = ArrayMax(boosterPacks);

        // The shop this clause reads: the deck's vouchers plus its with: ones. Built once,
        // above the antes, and only when a shop slot is read.
        MotelyRunState? shopState =
            shopItems.Length > 0 ? ctx.Deck.GetRunState(held.Vouchers) : null;

        foreach (int ante in antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

            if (shopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante, shopState!);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (!ArrayContains(shopItems, slot))
                        continue;
                    count += MatchJoker(item, targetTypes, edition, stickers);
                }
            }

            if (boosterPacks.Length > 0)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                var jokerStream = ctx.CreateBuffoonPackJokerStream(ante);
                for (int packIndex = 0; packIndex <= maxPack; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Buffoon)
                        continue;
                    var packSize = pack.GetPackSize();
                    var contents = ctx.GetNextBuffoonPackContents(
                        ref jokerStream,
                        packSize
                    );
                    if (
                        !ArrayContains(boosterPacks, packIndex)
                        || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                    )
                        continue;
                    for (int i = 0; i < contents.Length; i++)
                    {
                        count += MatchJoker(contents[i], targetTypes, edition, stickers);
                    }
                }
            }

            count += CountSpecialtyJokerSources(
                ref ctx,
                ante,
                sources,
                targetTypes,
                edition,
                stickers,
                runState
            );
        }

        return count;
    }

    private static int CountSpecialtyJokerSources(
        ref MotelySingleSearchContext ctx,
        int ante,
        JokerSourceConfig sources,
        MotelyItemType[] targetTypes,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
    {
        int count = 0;

        if (sources.Judgement.Length > 0)
        {
            int max = ArrayMax(sources.Judgement);
            var stream = ctx.CreateJudgementJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Judgement, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.Wraith.Length > 0)
        {
            int max = ArrayMax(sources.Wraith);
            var stream = ctx.CreateWraithJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Wraith, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.RiffRaff.Length > 0)
        {
            int max = ArrayMax(sources.RiffRaff);
            var stream = ctx.CreateRiffRaffJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RiffRaff, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.RareTag.Length > 0)
        {
            int max = ArrayMax(sources.RareTag);
            var stream = ctx.CreateRareTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareTag, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.UncommonTag.Length > 0)
        {
            int max = ArrayMax(sources.UncommonTag);
            var stream = ctx.CreateUncommonTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonTag, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.CommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.CommonShopJokers);
            var stream = ctx.CreateCommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.CommonShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.UncommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.UncommonShopJokers);
            var stream = ctx.CreateUncommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.RareShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.RareShopJokers);
            var stream = ctx.CreateRareShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.AllShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.AllShopJokers);
            var stream = ctx.CreateShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.AllShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        return count;
    }

    private static int CountJokerOccurrencesWildcard(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState,
        MotelyWith held
    )
    {
        int count = 0;
        var shopItems = sources.ShopItems;
        var boosterPacks = sources.BoosterPacks;

        int maxShop = ArrayMax(shopItems);
        int userMaxPack = ArrayMax(boosterPacks);

        // The shop this clause reads: the deck's vouchers plus its with: ones. Built once,
        // above the antes, and only when a shop slot is read.
        MotelyRunState? shopState =
            shopItems.Length > 0 ? ctx.Deck.GetRunState(held.Vouchers) : null;

        foreach (int ante in antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

            if (shopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante, shopState!);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (!ArrayContains(shopItems, slot))
                        continue;
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                }
            }

            if (boosterPacks.Length > 0)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                var jokerStream = ctx.CreateBuffoonPackJokerStream(ante);
                for (int packIndex = 0; packIndex <= maxPack; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Buffoon)
                        continue;
                    var packSize = pack.GetPackSize();
                    var contents = ctx.GetNextBuffoonPackContents(
                        ref jokerStream,
                        packSize
                    );
                    if (
                        !ArrayContains(boosterPacks, packIndex)
                        || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                    )
                        continue;
                    for (int i = 0; i < contents.Length; i++)
                    {
                        count += MatchJokerWildcard(contents[i], wildcardRarity, edition, stickers);
                    }
                }
            }

            count += CountSpecialtyJokerSourcesWildcard(
                ref ctx,
                ante,
                sources,
                wildcardRarity,
                edition,
                stickers,
                runState
            );
        }

        return count;
    }

    private static int CountSpecialtyJokerSourcesWildcard(
        ref MotelySingleSearchContext ctx,
        int ante,
        JokerSourceConfig sources,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
    {
        int count = 0;

        if (sources.Judgement.Length > 0)
        {
            int max = ArrayMax(sources.Judgement);
            var stream = ctx.CreateJudgementJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Judgement, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.Wraith.Length > 0)
        {
            int max = ArrayMax(sources.Wraith);
            var stream = ctx.CreateWraithJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Wraith, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.RiffRaff.Length > 0)
        {
            int max = ArrayMax(sources.RiffRaff);
            var stream = ctx.CreateRiffRaffJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RiffRaff, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.RareTag.Length > 0)
        {
            int max = ArrayMax(sources.RareTag);
            var stream = ctx.CreateRareTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareTag, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.UncommonTag.Length > 0)
        {
            int max = ArrayMax(sources.UncommonTag);
            var stream = ctx.CreateUncommonTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonTag, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.CommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.CommonShopJokers);
            var stream = ctx.CreateCommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.CommonShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        if (sources.UncommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.UncommonShopJokers);
            var stream = ctx.CreateUncommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        if (sources.RareShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.RareShopJokers);
            var stream = ctx.CreateRareShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        if (sources.AllShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.AllShopJokers);
            var stream = ctx.CreateShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.AllShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        return count;
    }

    private static int MatchJoker(
        MotelyItem item,
        MotelyItemType[] targetTypes,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers
    )
    {
        if (item.TypeCategory != MotelyItemTypeCategory.Joker)
            return 0;
        if (edition.HasValue && item.Edition != edition.Value)
            return 0;
        for (int i = 0; i < stickers.Length; i++)
        {
            bool hasSticker = stickers[i] switch
            {
                MotelyJokerSticker.Eternal => item.IsEternal,
                MotelyJokerSticker.Perishable => item.IsPerishable,
                MotelyJokerSticker.Rental => item.IsRental,
                _ => true,
            };
            if (!hasSticker)
                return 0;
        }

        int matches = 0;
        for (int i = 0; i < targetTypes.Length; i++)
            if (item.Type == targetTypes[i])
                matches++;
        return matches;
    }

    private static int MatchJokerWildcard(
        MotelyItem item,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers
    )
    {
        if (item.TypeCategory != MotelyItemTypeCategory.Joker)
            return 0;
        if (
            wildcardRarity.HasValue
            && (MotelyJokerRarity)(item.Value & MotelyGlobals.JokerRarityMask)
                != wildcardRarity.Value
        )
            return 0;
        if (edition.HasValue && item.Edition != edition.Value)
            return 0;
        for (int i = 0; i < stickers.Length; i++)
        {
            bool hasSticker = stickers[i] switch
            {
                MotelyJokerSticker.Eternal => item.IsEternal,
                MotelyJokerSticker.Perishable => item.IsPerishable,
                MotelyJokerSticker.Rental => item.IsRental,
                _ => true,
            };
            if (!hasSticker)
                return 0;
        }
        return 1;
    }

}
