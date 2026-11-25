using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters;

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class BossClause: MotelyClause,IAnteScopedClause
{
    public MotelyBossBlind[]? Boss { get => Bosses; set => Bosses = value ?? []; }

    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }

    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>The bosses that count; empty (<c>any</c>) counts every ante's boss.</summary>
    public MotelyBossBlind[] Bosses { get; set; } = [];
    // No Rolls: boss re-rolls need the full-run rewind that MotelySearchContext.Boss.cs does
    // not simulate yet. Antes select the where.

    public override IMotelySeedFilterDesc CreateFilterDesc() => new BossFilterDesc(this);
}

public readonly struct BossFilterDesc(BossClause clause)
    : IMotelySeedFilterDesc<BossFilterDesc.BossFilter>
{
    private readonly BossClause _clause = clause;

    public BossFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
        new BossFilter(_clause);

    public struct BossFilter(BossClause clause) : IMotelySeedFilter
    {
        private readonly BossClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Single match core: same PrepareRunState + CountBossOccurrences as should-scoring.
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    ClauseScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}
