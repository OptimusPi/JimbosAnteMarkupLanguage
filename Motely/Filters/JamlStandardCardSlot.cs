namespace Motely.Filters;

/// <summary>
/// <c>standardCard:</c> as a whole card scalar, <c>any</c>, or a nested <c>{ rank, suit, … }</c> block.
/// </summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed class JamlStandardCardSlot
{
    public MotelyStandardCard? Card { get; set; }
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
    public MotelyItemEnhancement? Enhancement { get; set; }
    public MotelyItemSeal? Seal { get; set; }
    public MotelyItemEdition? Edition { get; set; }
}
