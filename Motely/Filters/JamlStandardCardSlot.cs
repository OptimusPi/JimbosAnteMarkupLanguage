using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

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

/// <summary>
/// The scalar forms of <see cref="JamlStandardCardSlot"/>: <c>standardCard: any</c> (no card pinned)
/// and <c>standardCard: KingOfHearts</c>. A <c>{ rank, suit, … }</c> block is left to the generated
/// object deserializer.
/// </summary>
internal sealed class JamlStandardCardSlotScalarDeserializer : INodeDeserializer
{
    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer
    )
    {
        value = null;
        if (expectedType != typeof(JamlStandardCardSlot) || !reader.TryConsume<Scalar>(out var scalar))
            return false;

        if (JamlClauseBinding.IsAnyWord(scalar.Value))
        {
            value = new JamlStandardCardSlot();
            return true;
        }

        try
        {
            value = new JamlStandardCardSlot { Card = JamlEnumName.Parse<MotelyStandardCard>(scalar.Value) };
        }
        catch (FormatException ex)
        {
            throw new YamlException(scalar.Start, scalar.End, ex.Message, ex);
        }
        return true;
    }
}
