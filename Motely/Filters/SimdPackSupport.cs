using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters;

/// <summary>
/// Shared SIMD pack-walk helpers: ante-1 Hieroglyph/Petroglyph reachability and
/// per-lane pack-size PRNG masks so vector prefilters stop under-counting Jumbo/Mega
/// and stop counting ante-1 slots 4–5 on non-extended lanes.
/// </summary>
internal static class SimdPackSupport
{
    /// <summary>
    /// Lanes where ante-2 Hieroglyph or Petroglyph extends ante-1 pack slots to 4–7
    /// (same voucher path as <see cref="ClauseScoring"/> PrepareRunState, including the bonus
    /// voucher an ante-1 Hieroglyph draws before ante 2's voucher is rolled).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask Ante1PackExtensionMask(ref MotelyVectorSearchContext ctx)
    {
        var state = ctx.Deck.GetDefaultVectorRunState();
        var voucherStream = ctx.CreateVoucherStream(1);
        var ante1 = ctx.GetNextVoucher(ref voucherStream, state);
        state.ActivateVoucher(ante1);
        VectorMask ante1Reduces =
            VectorEnum256.Equals(ante1, MotelyVoucher.Hieroglyph)
            | VectorEnum256.Equals(ante1, MotelyVoucher.Petroglyph);
        if (ante1Reduces.IsPartiallyTrue())
        {
            var bonus = ctx.GetNextVoucher(ref voucherStream, state);
            state.ActivateVoucher(bonus, ante1Reduces);
        }
        var ante2 = ctx.GetAnteFirstVoucher(2, state);
        return VectorEnum256.Equals(ante2, MotelyVoucher.Hieroglyph)
            | VectorEnum256.Equals(ante2, MotelyVoucher.Petroglyph);
    }

    /// <summary>
    /// Per-lane whether pack index is reachable this ante — the same ceiling scoring's
    /// <c>ClampBoosterPackSlotForAnte</c> applies: slots 0–5 on every ante but 1; ante 1 slots
    /// 0–3 always and 4–7 only with the Hieroglyph/Petroglyph extension.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask SlotReachableMask(
        int ante,
        int packIndex,
        VectorMask ante1Extended
    )
    {
        if (ante != 1)
            return packIndex <= MotelyGlobals.LateAntesMaxPackSlot
                ? VectorMask.AllBitsSet
                : VectorMask.NoBitsSet;
        if (packIndex <= MotelyGlobals.EarlyAnteMaxPackSlot)
            return VectorMask.AllBitsSet;
        return packIndex <= Ante1ExtendedMaxPackSlot ? ante1Extended : VectorMask.NoBitsSet;
    }

    /// <summary>Ante 1 with the extension walks both of its shops' packs twice over.</summary>
    public const int Ante1ExtendedMaxPackSlot = 2 * (MotelyGlobals.EarlyAnteMaxPackSlot + 1) - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> ToPrngMask(VectorMask mask) =>
        ExtendIntMaskToDouble(VectorMaskToConditionalSelectMask(mask));

    /// <summary>
    /// Whether the filter needs ante-1 extension masks (requested pack index past early ante cap).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NeedsAnte1Extension(int maxBoosterPack) =>
        maxBoosterPack > MotelyGlobals.EarlyAnteMaxPackSlot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddMatchCounts(
        VectorMask matched,
        ref Vector256<int> matchCounts
    )
    {
        if (matched.IsAllFalse())
            return;
        matchCounts = Vector256.Add(
            matchCounts,
            Vector256.ConditionalSelect(
                VectorMaskToConditionalSelectMask(matched),
                Vector256.Create(1),
                Vector256<int>.Zero
            )
        );
    }

    /// <summary>Lanes holding a real seed in this batch (the tail batch can be partial).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask ValidLaneMask(ref MotelyVectorSearchContext ctx)
    {
        uint bits = 0;
        for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
        {
            if (ctx.IsLaneValid(lane))
                bits |= 1u << lane;
        }

        return new VectorMask(bits);
    }

    /// <summary>
    /// Vector form of match bounds: count ≥ min, and count ≤ max when max is set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask MeetsMinMaxMask(Vector256<int> matchCounts, int min, int? max)
    {
        Debug.Assert(min > 0);
        var minOk = Vector256.GreaterThan(matchCounts, Vector256.Create(min - 1));
        if (max is null)
            return new VectorMask(VectorizedComparisonToMask(minOk));
        var maxOk = Vector256.LessThanOrEqual(matchCounts, Vector256.Create(max.Value));
        return new VectorMask(VectorizedComparisonToMask(Vector256.BitwiseAnd(minOk, maxOk)));
    }

    public const MotelyShopStreamFlags JokerOnlyShopFlags =
        MotelyShopStreamFlags.ExcludeTarots
        | MotelyShopStreamFlags.ExcludePlanets
        | MotelyShopStreamFlags.ExcludeSpectrals
        | MotelyShopStreamFlags.ExcludeStandardCards;

    public const MotelyShopStreamFlags TarotOnlyShopFlags =
        MotelyShopStreamFlags.ExcludeJokers
        | MotelyShopStreamFlags.ExcludePlanets
        | MotelyShopStreamFlags.ExcludeSpectrals
        | MotelyShopStreamFlags.ExcludeStandardCards;

    public const MotelyShopStreamFlags PlanetOnlyShopFlags =
        MotelyShopStreamFlags.ExcludeJokers
        | MotelyShopStreamFlags.ExcludeTarots
        | MotelyShopStreamFlags.ExcludeSpectrals
        | MotelyShopStreamFlags.ExcludeStandardCards;

    public const MotelyShopStreamFlags SpectralOnlyShopFlags =
        MotelyShopStreamFlags.ExcludeJokers
        | MotelyShopStreamFlags.ExcludeTarots
        | MotelyShopStreamFlags.ExcludePlanets
        | MotelyShopStreamFlags.ExcludeStandardCards;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasActiveStickers(MotelyJokerSticker[] stickers)
    {
        for (int i = 0; i < stickers.Length; i++)
        {
            if (stickers[i] != MotelyJokerSticker.None)
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MotelyJokerStreamFlags GetJokerStreamFlags(
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyJokerStreamFlags rarityExclusions
    )
    {
        var flags = rarityExclusions;
        if (!edition.HasValue)
            flags |= MotelyJokerStreamFlags.ExcludeEdition;
        if (!HasActiveStickers(stickers))
            flags |= MotelyJokerStreamFlags.ExcludeStickers;
        return flags;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MotelyJokerFixedRarityStreamFlags GetFixedJokerStreamFlags(
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers
    )
    {
        var flags = MotelyJokerFixedRarityStreamFlags.Default;
        if (!edition.HasValue)
            flags |= MotelyJokerFixedRarityStreamFlags.ExcludeEdition;
        if (!HasActiveStickers(stickers))
            flags |= MotelyJokerFixedRarityStreamFlags.ExcludeStickers;
        return flags;
    }

    public static MotelyJokerStreamFlags GetRarityExclusions(MotelyItemType[] targetTypes)
    {
        if (targetTypes.Length == 0)
            return MotelyJokerStreamFlags.Default;

        bool hasCommon = false;
        bool hasUncommon = false;
        bool hasRare = false;

        for (int i = 0; i < targetTypes.Length; i++)
        {
            var rarity = (MotelyJokerRarity)((int)targetTypes[i] & MotelyGlobals.JokerRarityMask);
            switch (rarity)
            {
                case MotelyJokerRarity.Common:
                    hasCommon = true;
                    break;
                case MotelyJokerRarity.Uncommon:
                    hasUncommon = true;
                    break;
                case MotelyJokerRarity.Rare:
                    hasRare = true;
                    break;
            }
        }

        var flags = MotelyJokerStreamFlags.Default;
        if (!hasCommon)
            flags |= MotelyJokerStreamFlags.ExcludeCommon;
        if (!hasUncommon)
            flags |= MotelyJokerStreamFlags.ExcludeUncommon;
        if (!hasRare)
            flags |= MotelyJokerStreamFlags.ExcludeRare;
        return flags;
    }

    public static void CacheSpecialtyJokerStreams(
        ref MotelyFilterCreationContext ctx,
        int ante,
        JokerSourceConfig sources,
        MotelyJokerStreamFlags jokerFlags,
        MotelyJokerFixedRarityStreamFlags fixedFlags
    )
    {
        if (sources.CommonShopJokers.Length > 0)
            ctx.CacheCommonShopJokerStream(ante, fixedFlags);
        if (sources.UncommonShopJokers.Length > 0)
            ctx.CacheUncommonShopJokerStream(ante, fixedFlags);
        if (sources.RareShopJokers.Length > 0)
            ctx.CacheRareShopJokerStream(ante, fixedFlags);
        if (sources.AllShopJokers.Length > 0)
            ctx.CacheShopJokerStream(ante, jokerFlags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MaxSlot(int[] indices)
    {
        int max = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] > max)
                max = indices[i];
        }
        return max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsIndex(int[] indices, int value)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] == value)
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask MatchJokers(
        in MotelyItemVector item,
        MotelyItemType[] targetTypes,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers
    )
    {
        VectorMask jokerMatch;
        if (targetTypes.Length == 0)
        {
            jokerMatch = VectorEnum256.Equals(item.TypeCategory, MotelyItemTypeCategory.Joker);
            if (wildcardRarity.HasValue)
            {
                var rarityVec = new VectorEnum256<MotelyJokerRarity>(
                    Vector256.BitwiseAnd(
                        item.Value,
                        Vector256.Create(MotelyGlobals.JokerRarityMask)
                    )
                );
                jokerMatch &= VectorEnum256.Equals(rarityVec, wildcardRarity.Value);
            }
        }
        else
        {
            jokerMatch = VectorMask.NoBitsSet;
            for (int t = 0; t < targetTypes.Length; t++)
                jokerMatch |= VectorEnum256.Equals(item.Type, targetTypes[t]);
        }

        if (edition.HasValue)
            jokerMatch &= VectorEnum256.Equals(item.Edition, edition.Value);

        for (int s = 0; s < stickers.Length; s++)
        {
            switch (stickers[s])
            {
                case MotelyJokerSticker.Eternal:
                    jokerMatch &= item.IsEternal;
                    break;
                case MotelyJokerSticker.Perishable:
                    jokerMatch &= item.IsPerishable;
                    break;
                case MotelyJokerSticker.Rental:
                    jokerMatch &= item.IsRental;
                    break;
            }
        }

        return jokerMatch;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WalkSpecialtyJokerSources(
        ref MotelyVectorSearchContext ctx,
        int ante,
        JokerSourceConfig sources,
        MotelyItemType[] targetTypes,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyJokerStreamFlags jokerFlags,
        MotelyJokerFixedRarityStreamFlags fixedFlags,
        ref Vector256<int> matchCounts
    )
    {
        bool canMatchCommon = !jokerFlags.HasFlag(MotelyJokerStreamFlags.ExcludeCommon);
        bool canMatchUncommon = !jokerFlags.HasFlag(MotelyJokerStreamFlags.ExcludeUncommon);
        bool canMatchRare = !jokerFlags.HasFlag(MotelyJokerStreamFlags.ExcludeRare);

        if (sources.Judgement.Length > 0)
        {
            int max = MaxSlot(sources.Judgement);
            var stream = ctx.CreateJudgementJokerStream(ante, jokerFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.Judgement, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.Wraith.Length > 0 && canMatchRare)
        {
            int max = MaxSlot(sources.Wraith);
            var stream = ctx.CreateWraithJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.Wraith, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.RiffRaff.Length > 0 && canMatchCommon)
        {
            int max = MaxSlot(sources.RiffRaff);
            var stream = ctx.CreateRiffRaffJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.RiffRaff, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.RareTag.Length > 0 && canMatchRare)
        {
            int max = MaxSlot(sources.RareTag);
            var stream = ctx.CreateRareTagJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.RareTag, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.UncommonTag.Length > 0 && canMatchUncommon)
        {
            int max = MaxSlot(sources.UncommonTag);
            var stream = ctx.CreateUncommonTagJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.UncommonTag, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.CommonShopJokers.Length > 0 && canMatchCommon)
        {
            int max = MaxSlot(sources.CommonShopJokers);
            var stream = ctx.CreateCommonShopJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.CommonShopJokers, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.UncommonShopJokers.Length > 0 && canMatchUncommon)
        {
            int max = MaxSlot(sources.UncommonShopJokers);
            var stream = ctx.CreateUncommonShopJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.UncommonShopJokers, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.RareShopJokers.Length > 0 && canMatchRare)
        {
            int max = MaxSlot(sources.RareShopJokers);
            var stream = ctx.CreateRareShopJokerStream(ante, fixedFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.RareShopJokers, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }

        if (sources.AllShopJokers.Length > 0)
        {
            int max = MaxSlot(sources.AllShopJokers);
            var stream = ctx.CreateShopJokerStream(ante, jokerFlags);
            for (int idx = 0; idx <= max; idx++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (!ContainsIndex(sources.AllShopJokers, idx))
                    continue;
                AddMatchCounts(
                    MatchJokers(item, targetTypes, wildcardRarity, edition, stickers),
                    ref matchCounts
                );
            }
        }
    }
}

/// <summary>
/// The SIMD Buffoon-pack walk draws each card straight off the pack stream and does not resample
/// in-pack duplicates the way Balatro (and the scalar walk) does. Where a lane drew a duplicate in
/// a counted pack, the real card is a resample that may or may not match, so the vector count is
/// unknowable for that lane. Track those lanes and pass them through to the scalar confirm instead
/// of rejecting them — a prefilter may over-accept, never under-accept.
/// </summary>
internal struct BuffoonPackDuplicateTracker
{
    private MotelyItemVector _first;
    private MotelyItemVector _second;
    private MotelyItemVector _third;
    private int _count;

    /// <summary>Start a new pack.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => _count = 0;

    /// <summary>Records <paramref name="joker"/> and returns the lanes where it repeats an earlier card of this pack.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorMask Add(in MotelyItemVector joker)
    {
        VectorMask validJoker = ~VectorEnum256.Equals(
            joker.Type,
            MotelyItemType.JokerExcludedByStream
        );
        VectorMask duplicate = VectorMask.NoBitsSet;
        if (_count > 0)
            duplicate |= VectorEnum256.Equals(joker.Type, _first.Type) & validJoker;
        if (_count > 1)
            duplicate |= VectorEnum256.Equals(joker.Type, _second.Type) & validJoker;
        if (_count > 2)
            duplicate |= VectorEnum256.Equals(joker.Type, _third.Type) & validJoker;

        switch (_count)
        {
            case 0:
                _first = joker;
                break;
            case 1:
                _second = joker;
                break;
            case 2:
                _third = joker;
                break;
        }

        _count++;
        return duplicate;
    }
}
