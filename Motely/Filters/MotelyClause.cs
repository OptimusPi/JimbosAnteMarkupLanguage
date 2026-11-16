namespace Motely.Filters;

/// <summary>A filter's parameters: what a seed must contain, and how much a hit is worth.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public partial class MotelyClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;

    /// <summary>The SIMD filter this clause installs. Each clause class names its own.</summary>
    public virtual IMotelySeedFilterDesc CreateFilterDesc() =>
        throw new InvalidOperationException(ClauseKeyDeserializer.NeedsKey);
}

/// <summary>
/// Capability for clauses scoped to specific antes (cards/features). Event clauses do NOT
/// implement this — they are roll-scoped, not ante-scoped.
/// </summary>
public interface IAnteScopedClause
{
    int[] Antes { get; set; }
}

/// <summary>
/// Capability for clauses scoped to specific PRNG roll indices instead of antes (the event
/// clauses — LuckyMoney, WheelOfFortune, MisprintMult, etc.).
/// </summary>
public interface IRollScopedClause
{
    int[] Rolls { get; set; }
}

/// <summary>Capability for clauses that take a <see cref="MotelyWith"/> block: what the player has on hand.</summary>
public interface IWithScopedClause
{
    MotelyWith With { get; set; }
}

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record MotelyWith
{
    /// <summary>Oops! All 6s multiplier — each Oops doubles the odds (X2, X4, X8…). X1 = base odds.
    /// Read by the luck event clauses.</summary>
    public MotelyLuck Luck { get; set; } = MotelyLuck.X1;

    /// <summary>
    /// Jokers the player is holding. Read by <c>legendaryJoker</c>: a held legendary is out of the
    /// Soul's pool, so a draw that lands on it resamples (<c>Joker4_resample{n}</c>) — unless
    /// Showman is held too, which keeps duplicates in the pool.
    /// </summary>
    public MotelyJoker[] Jokers { get; set; } = [];

    /// <summary>
    /// Vouchers the player has redeemed, on top of the deck's own. Read by the clauses that look
    /// at the shop: Tarot Merchant/Tycoon, Planet Merchant/Tycoon and Magic Trick change what the
    /// shop rolls. A clause redeems them on the engine's run state
    /// (<see cref="MotelyDeckExt.GetRunState"/>) and rolls its shop from that.
    /// </summary>
    public MotelyVoucher[] Vouchers { get; set; } = [];
}
