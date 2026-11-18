using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters;

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class VoucherClause: MotelyClause,IAnteScopedClause, IRollScopedClause
{
    public MotelyVoucher[]? Voucher { get => Vouchers; set => Vouchers = value ?? []; }

    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }

    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>The vouchers that count; empty (<c>any</c>) counts every draw.</summary>
    public MotelyVoucher[] Vouchers { get; set; } = [];

    /// <summary>
    /// Voucher-stream indices per ante: 0 = ante award, 1+ = further draws on that ante's
    /// voucher stream (Hieroglyph bonus, voucher-tag shop extras, etc.).
    /// </summary>
    public int[] Rolls { get; set; } = [0];

    public override IMotelySeedFilterDesc CreateFilterDesc() => new VoucherFilterDesc(this);
}

public struct VoucherFilterDesc(VoucherClause clause)
    : IMotelySeedFilterDesc<VoucherFilterDesc.VoucherFilter>
{
    private readonly VoucherClause _clause = clause;

    public readonly VoucherFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        int maxAnte = 0;
        for (int i = 0; i < _clause.Antes.Length; i++)
        {
            if (_clause.Antes[i] > maxAnte)
                maxAnte = _clause.Antes[i];
        }

        // Cache all antes 1..maxAnte so state-building passes on non-target antes work.
        for (int ante = 1; ante <= maxAnte; ante++)
            ctx.CacheAnteFirstVoucher(ante);

        return new VoucherFilter(_clause, maxAnte);
    }

    public struct VoucherFilter(VoucherClause clause, int maxAnte) : IMotelySeedFilter
    {
        private readonly VoucherClause _clause = clause;
        private readonly int _maxAnte = maxAnte;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            int maxAnte = _maxAnte;

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var voucherState = ctx.Deck.GetDefaultVectorRunState();

            for (int ante = 1; ante <= maxAnte; ante++)
            {
                bool isTarget = false;
                for (int i = 0; i < clause.Antes.Length; i++)
                {
                    if (clause.Antes[i] == ante)
                    {
                        isTarget = true;
                        break;
                    }
                }
                // Roll 0 is the first draw of this ante's stream; later rolls continue it.
                var voucherStream = ctx.CreateVoucherStream(ante);
                var vouchers = ctx.GetNextVoucher(ref voucherStream, voucherState);
                voucherState.ActivateVoucher(vouchers);

                // Hieroglyph/Petroglyph send the run back an ante, so the same shop offers a
                // bonus voucher: the very next draw on this stream, with the glyph already
                // redeemed (mirrors ClauseScoring.ApplyPrepareRunState). That bonus IS roll 1 --
                // AccumulateVoucherRolls draws it, not a separate call, or every later roll
                // index would be shifted off by one.
                VectorMask glyphMask = (VectorMask)(
                    VectorEnum256.Equals(vouchers, MotelyVoucher.Hieroglyph)
                    | VectorEnum256.Equals(vouchers, MotelyVoucher.Petroglyph)
                );
                bool anyGlyph = !glyphMask.IsAllFalse();

                if (!isTarget && !anyGlyph)
                    continue;

                matchCounts = AccumulateVoucherRolls(
                    ref ctx,
                    ref voucherStream,
                    ref voucherState,
                    vouchers,
                    clause,
                    matchCounts,
                    isTarget,
                    glyphMask,
                    anyGlyph
                );
            }

            return SimdPackSupport.MeetsMinMaxMask(matchCounts, clause.Min, clause.Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> AccumulateVoucherRolls(
            ref MotelyVectorSearchContext ctx,
            ref MotelyVectorVoucherStream voucherStream,
            ref MotelyVectorRunState voucherState,
            VectorEnum256<MotelyVoucher> anteVouchers,
            VoucherClause clause,
            Vector256<int> matchCounts,
            bool isTarget,
            VectorMask glyphMask,
            bool anyGlyph
        )
        {
            // Walk the voucher stream once, index 0..maxRoll, mirroring the scalar
            // CountVoucherOccurrences. Index 0 is the ante award, already drawn from
            // voucherStream; 1+ are the draws after it on the same stream. When any lane in
            // this batch rolled a glyph, roll 1 must be drawn even if the clause itself never
            // asked for it, so the bonus gets activated into run state for those lanes.
            // (stackalloc of VectorEnum256 is the same pattern TagFilter uses for its tag-stream
            // draws.)
            int maxRoll = clause.Rolls.Length == 0 ? 0 : clause.Rolls.Max();
            int neededMax = anyGlyph ? Math.Max(maxRoll, 1) : maxRoll;
            Span<VectorEnum256<MotelyVoucher>> draws =
                stackalloc VectorEnum256<MotelyVoucher>[neededMax + 1];
            draws[0] = anteVouchers;

            for (int i = 1; i <= neededMax; i++)
            {
                draws[i] = ctx.GetNextVoucher(ref voucherStream, voucherState);
                if (i == 1 && anyGlyph)
                    voucherState.ActivateVoucher(draws[1], glyphMask); // the bonus is real, forced
            }

            if (!isTarget)
                return matchCounts;

            bool rolledOne = false;
            foreach (var roll in clause.Rolls)
            {
                if (roll == 1) rolledOne = true;
                matchCounts = AddVoucherMatches(matchCounts, draws[roll], clause);
            }

            // The bonus always appears in the real shop for this ante, so count it for glyph
            // lanes even when the clause's Rolls didn't explicitly ask for roll 1.
            if (anyGlyph && !rolledOne)
                matchCounts = AddVoucherMatches(matchCounts, draws[1], clause, glyphMask);

            return matchCounts;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> AddVoucherMatches(
            Vector256<int> counts,
            VectorEnum256<MotelyVoucher> vouchers,
            VoucherClause clause,
            VectorMask? includeMask = null
        )
        {
            // Vector256.Equals lanes are all-ones (-1) or 0, so the union of the per-name
            // masks is a bitwise OR; a signed Max would pick 0 over -1 and empty the union.
            // No names at all is `any`: every draw counts.
            Vector256<int> matchMask = clause.Vouchers.Length == 0 ? Vector256<int>.AllBitsSet : Vector256<int>.Zero;
            foreach (var v in clause.Vouchers)
                matchMask = Vector256.BitwiseOr(matchMask, VectorEnum256.Equals(vouchers, v));

            if (includeMask.HasValue)
            {
                matchMask = Vector256.BitwiseAnd(
                    matchMask,
                    MotelyVectorUtils.VectorMaskToConditionalSelectMask(includeMask.Value)
                );
            }

            return Vector256.Add(
                counts,
                Vector256.ConditionalSelect(matchMask, Vector256.Create(1), Vector256<int>.Zero)
            );
        }
    }
}
