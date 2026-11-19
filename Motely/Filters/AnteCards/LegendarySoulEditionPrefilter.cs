using System;
using System.Diagnostics;
using Motely;

namespace Motely.Filters;

/// <summary>
/// Vector edition-only prefilter for legendary soul joker streams (ExcludeJokerType | ExcludeStickers).
/// Shared by <see cref="LegendarySoulEditionFilterDesc"/> and <see cref="LegendaryJokerFilterDesc"/>.
/// </summary>
internal static class LegendarySoulEditionPrefilter
{
    internal static int GetEditionSoulRollCount(LegendaryJokerClause clause)
    {
        var sources = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;
        int maxSlot = sources.MaxReferencedBoosterSlot();
        // One edition roll per Soul opened: up to one per shop slot walked, plus one per Charm.
        int n = maxSlot >= 0 || sources.TagPacks.Length > 0
            ? maxSlot + 1 + sources.TagPacks.Length
            : 6;
        // soulEditionRolls, when written, is exactly how many Souls per ante get read:
        // 1 peeks at the first Soul only, a higher number finds packs holding two Souls.
        if (clause.SoulEditionRolls > 0)
            n = clause.SoulEditionRolls;
        return n;
    }

    internal static VectorMask Apply(
        ref MotelyVectorSearchContext ctx,
        LegendaryJokerClause clause,
        uint laneMask
    )
    {
        Debug.Assert(clause.Edition.HasValue);

        VectorMask candidateMask = new VectorMask(laneMask);
        var targetEdition = clause.Edition.Value;
        VectorMask editionOk = VectorMask.NoBitsSet;
        int maxSouls = GetEditionSoulRollCount(clause);

        foreach (var ante in clause.Antes)
        {
            var soulStream = ctx.CreateLegendaryJokerStream(
                ante,
                MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                    | MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                isCached: true
            );
            VectorMask anteAny = VectorMask.NoBitsSet;
            for (int s = 0; s < maxSouls; s++)
            {
                var editionVec = ctx.GetNextJoker(ref soulStream).Edition;
                anteAny |= VectorEnum256.Equals(editionVec, targetEdition);
            }

            editionOk |= anteAny;
        }

        candidateMask &= editionOk;
        return candidateMask;
    }

    internal static bool HasHeldLegendaries(MotelyJoker[]? jokers)
    {
        if (jokers is null || jokers.Length == 0)
            return false;
        for (int i = 0; i < jokers.Length; i++)
        {
            if (
                ((MotelyJokerRarity)((int)jokers[i] & MotelyGlobals.JokerRarityMask))
                == MotelyJokerRarity.Legendary
            )
                return true;
        }
        return false;
    }

    private static bool HasShowman(MotelyJoker[]? jokers)
    {
        if (jokers is null || jokers.Length == 0)
            return false;
        for (int i = 0; i < jokers.Length; i++)
        {
            if (jokers[i] == MotelyJoker.Showman)
                return true;
        }
        return false;
    }

    internal static VectorMask ApplySoulAndFacePrefilter(
        ref MotelyVectorSearchContext ctx,
        LegendaryJokerClause clause,
        VectorMask candidateMask
    )
    {
        if (candidateMask.IsAllFalse())
            return candidateMask;

        var src = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;
        bool split = src.ArcanaPacks.Length > 0 || src.SpectralPacks.Length > 0;
        bool checkArcana =
            (!split && src.BoosterPacks.Length > 0)
            || src.ArcanaPacks.Length > 0
            || src.TagPacks.Length > 0;
        bool checkSpectral =
            (!split && src.BoosterPacks.Length > 0) || src.SpectralPacks.Length > 0;

        if (!checkArcana && !checkSpectral)
            return VectorMask.NoBitsSet;

        System.Runtime.Intrinsics.Vector256<int> soulHits =
            System.Runtime.Intrinsics.Vector256<int>.Zero;
        var threshold = System.Runtime.Intrinsics.Vector512.Create(0.997);
        var one = System.Runtime.Intrinsics.Vector256.Create(1);

        foreach (var ante in clause.Antes)
        {
            if (checkArcana)
            {
                int maxArcanaSlot =
                    src.ArcanaPacks.Length > 0
                        ? SimdPackSupport.MaxSlot(src.ArcanaPacks)
                        : (
                            !split && src.BoosterPacks.Length > 0
                                ? SimdPackSupport.MaxSlot(src.BoosterPacks)
                                : -1
                        );
                if (src.TagPacks.Length > 0)
                {
                    int tagInsert = MotelyGlobals.TagPackInsertSlot(ante);
                    if (tagInsert - 1 > maxArcanaSlot)
                        maxArcanaSlot = tagInsert - 1;
                }

                int arcanaPacksCount =
                    (maxArcanaSlot >= 0 ? maxArcanaSlot + 1 : 0)
                    + (src.TagPacks.Length > 0 ? SimdPackSupport.MaxSlot(src.TagPacks) + 1 : 0);
                int maxTarotDraws = 5 * arcanaPacksCount;

                var soulTarot = ctx.CreatePrngStream(
                    MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + ante,
                    isCached: true
                );
                for (int d = 0; d < maxTarotDraws; d++)
                {
                    var roll = ctx.GetNextRandom(ref soulTarot);
                    var hit = MotelyVectorUtils.ShrinkDoubleMaskToInt(
                        System.Runtime.Intrinsics.Vector512.GreaterThan(roll, threshold)
                    );
                    soulHits = System.Runtime.Intrinsics.Vector256.Add(
                        soulHits,
                        System.Runtime.Intrinsics.Vector256.BitwiseAnd(hit, one)
                    );
                }
            }

            if (checkSpectral)
            {
                int maxSpectralSlot =
                    src.SpectralPacks.Length > 0
                        ? SimdPackSupport.MaxSlot(src.SpectralPacks)
                        : SimdPackSupport.MaxSlot(src.BoosterPacks);
                int maxSpectralDraws = 8 * (maxSpectralSlot + 1);

                var soulSpectral = ctx.CreatePrngStream(
                    MotelyPrngKeys.SpectralSoulBlackHole + MotelyPrngKeys.Spectral + ante,
                    isCached: true
                );
                for (int d = 0; d < maxSpectralDraws; d++)
                {
                    var roll = ctx.GetNextRandom(ref soulSpectral);
                    var hit = MotelyVectorUtils.ShrinkDoubleMaskToInt(
                        System.Runtime.Intrinsics.Vector512.GreaterThan(roll, threshold)
                    );
                    soulHits = System.Runtime.Intrinsics.Vector256.Add(
                        soulHits,
                        System.Runtime.Intrinsics.Vector256.BitwiseAnd(hit, one)
                    );
                }
            }
        }

        int minSoulsRequired = clause.Min;
        if (clause.Rolls.Length > 0)
        {
            int minRoll = clause.Rolls[0];
            for (int i = 1; i < clause.Rolls.Length; i++)
            {
                if (clause.Rolls[i] < minRoll)
                    minRoll = clause.Rolls[i];
            }
            if (minRoll + clause.Min > minSoulsRequired)
                minSoulsRequired = minRoll + clause.Min;
        }

        candidateMask &= SimdPackSupport.MeetsMinMaxMask(soulHits, minSoulsRequired, max: null);
        if (candidateMask.IsAllFalse())
            return candidateMask;

        if (
            !clause.SoulCardOnly
            && clause.Jokers.Length > 0
            && !HasHeldLegendaries(clause.With.Jokers)
        )
        {
            var joker4Stream = ctx.CreatePrngStream(
                MotelyPrngKeys.JokerLegendary,
                isCached: true
            );
            var legBits = System.Runtime.Intrinsics.Vector256.Create(
                (int)MotelyJokerRarity.Legendary
            );
            var draw0 = System.Runtime.Intrinsics.Vector256.BitwiseOr(
                legBits,
                ctx.GetNextRandomInt(
                    ref joker4Stream,
                    0,
                    MotelyEnum<MotelyJokerLegendary>.ValueCount
                )
            );

            var match0 = System.Runtime.Intrinsics.Vector256<int>.Zero;
            for (int i = 0; i < clause.Jokers.Length; i++)
            {
                match0 |= System.Runtime.Intrinsics.Vector256.Equals(
                    draw0,
                    System.Runtime.Intrinsics.Vector256.Create((int)clause.Jokers[i])
                );
            }

            if (clause.Rolls is [0])
            {
                candidateMask &= new VectorMask(
                    MotelyVectorUtils.VectorizedComparisonToMask(match0)
                );
            }
            else
            {
                var draw1 = System.Runtime.Intrinsics.Vector256.BitwiseOr(
                    legBits,
                    ctx.GetNextRandomInt(
                        ref joker4Stream,
                        0,
                        MotelyEnum<MotelyJokerLegendary>.ValueCount
                    )
                );
                var match1 = System.Runtime.Intrinsics.Vector256<int>.Zero;
                for (int i = 0; i < clause.Jokers.Length; i++)
                {
                    match1 |= System.Runtime.Intrinsics.Vector256.Equals(
                        draw1,
                        System.Runtime.Intrinsics.Vector256.Create((int)clause.Jokers[i])
                    );
                }

                var twoOrMoreSouls = System.Runtime.Intrinsics.Vector256.GreaterThan(
                    soulHits,
                    one
                );
                var threeOrMoreSouls = System.Runtime.Intrinsics.Vector256.GreaterThan(
                    soulHits,
                    System.Runtime.Intrinsics.Vector256.Create(2)
                );

                System.Runtime.Intrinsics.Vector256<int> faceOk =
                    clause.Min == 1
                        ? (
                            match0
                            | (
                                twoOrMoreSouls
                                & (
                                    match1
                                    | System.Runtime.Intrinsics.Vector256.Equals(draw1, draw0)
                                )
                            )
                            | threeOrMoreSouls
                        )
                        : (match0 | threeOrMoreSouls);

                candidateMask &= new VectorMask(
                    MotelyVectorUtils.VectorizedComparisonToMask(faceOk)
                );
            }
        }

        return candidateMask;
    }

    internal static VectorMask ApplySoulOrderPrefilter(
        ref MotelyVectorSearchContext ctx,
        LegendarySoulOrderClause clause,
        uint laneMask
    )
    {
        VectorMask candidateMask = new VectorMask(laneMask);
        if (clause.Entries.Length == 0 || candidateMask.IsAllFalse())
            return candidateMask;

        if (!HasHeldLegendaries(clause.With?.Jokers))
        {
            var joker4Stream = ctx.CreatePrngStream(
                MotelyPrngKeys.JokerLegendary,
                isCached: true
            );
            var legBits = System.Runtime.Intrinsics.Vector256.Create(
                (int)MotelyJokerRarity.Legendary
            );
            var draw0 = System.Runtime.Intrinsics.Vector256.BitwiseOr(
                legBits,
                ctx.GetNextRandomInt(
                    ref joker4Stream,
                    0,
                    MotelyEnum<MotelyJokerLegendary>.ValueCount
                )
            );
            var entry0Ok = System.Runtime.Intrinsics.Vector256.Equals(
                draw0,
                System.Runtime.Intrinsics.Vector256.Create((int)clause.Entries[0].Legendary)
            );
            candidateMask &= new VectorMask(
                MotelyVectorUtils.VectorizedComparisonToMask(entry0Ok)
            );
            if (candidateMask.IsAllFalse())
                return candidateMask;

            if (clause.Entries.Length >= 2)
            {
                var draw1 = System.Runtime.Intrinsics.Vector256.BitwiseOr(
                    legBits,
                    ctx.GetNextRandomInt(
                        ref joker4Stream,
                        0,
                        MotelyEnum<MotelyJokerLegendary>.ValueCount
                    )
                );
                var entry1Ok = System.Runtime.Intrinsics.Vector256.Equals(
                    draw1,
                    System.Runtime.Intrinsics.Vector256.Create((int)clause.Entries[1].Legendary)
                );
                if (!HasShowman(clause.With?.Jokers))
                    entry1Ok |= System.Runtime.Intrinsics.Vector256.Equals(draw1, draw0);

                candidateMask &= new VectorMask(
                    MotelyVectorUtils.VectorizedComparisonToMask(entry1Ok)
                );
                if (candidateMask.IsAllFalse())
                    return candidateMask;
            }
        }

        int firstAnte = Array.Exists(
            clause.Entries,
            static e => Array.IndexOf(e.Antes, 0) >= 0
        )
            ? 0
            : 1;
        int maxAnte = clause.MaxAnte > 0 ? clause.MaxAnte : 8;
        int maxSlot =
            clause.BoosterPacks.Length > 0
                ? SimdPackSupport.MaxSlot(clause.BoosterPacks)
                : MotelyGlobals.LateAntesMaxPackSlot;
        int maxTarotDraws = 5 * (maxSlot + 1);
        int maxSpectralDraws = 8 * (maxSlot + 1);

        var soulHits = System.Runtime.Intrinsics.Vector256<int>.Zero;
        var threshold = System.Runtime.Intrinsics.Vector512.Create(0.997);
        var one = System.Runtime.Intrinsics.Vector256.Create(1);

        for (int ante = firstAnte; ante <= maxAnte; ante++)
        {
            var soulTarot = ctx.CreatePrngStream(
                MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + ante,
                isCached: true
            );
            for (int d = 0; d < maxTarotDraws; d++)
            {
                var roll = ctx.GetNextRandom(ref soulTarot);
                var hit = MotelyVectorUtils.ShrinkDoubleMaskToInt(
                    System.Runtime.Intrinsics.Vector512.GreaterThan(roll, threshold)
                );
                soulHits = System.Runtime.Intrinsics.Vector256.Add(
                    soulHits,
                    System.Runtime.Intrinsics.Vector256.BitwiseAnd(hit, one)
                );
            }

            var soulSpectral = ctx.CreatePrngStream(
                MotelyPrngKeys.SpectralSoulBlackHole + MotelyPrngKeys.Spectral + ante,
                isCached: true
            );
            for (int d = 0; d < maxSpectralDraws; d++)
            {
                var roll = ctx.GetNextRandom(ref soulSpectral);
                var hit = MotelyVectorUtils.ShrinkDoubleMaskToInt(
                    System.Runtime.Intrinsics.Vector512.GreaterThan(roll, threshold)
                );
                soulHits = System.Runtime.Intrinsics.Vector256.Add(
                    soulHits,
                    System.Runtime.Intrinsics.Vector256.BitwiseAnd(hit, one)
                );
            }
        }

        candidateMask &= SimdPackSupport.MeetsMinMaxMask(
            soulHits,
            clause.Entries.Length,
            max: null
        );
        return candidateMask;
    }
}
