using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>
/// <c>standardCard:</c> as a whole card scalar, <c>any</c>, or a nested <c>{ rank, suit, … }</c> block.
/// </summary>
[YamlSerializable]
public sealed class JamlStandardCardSlot
{
    [YamlIgnore]
    public MotelyStandardCard? Card { get; set; }
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
    public MotelyItemEnhancement? Enhancement { get; set; }
    public MotelyItemSeal? Seal { get; set; }
    public MotelyItemEdition? Edition { get; set; }
}

internal sealed class JamlStandardCardSlotNodeDeserializer : INodeDeserializer
{
    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer)
    {
        if (expectedType != typeof(JamlStandardCardSlot) || !reader.Accept<Scalar>(out var scalar))
        {
            value = null;
            return false;
        }

        if (JamlClauseBinding.IsCategoryAny(scalar))
        {
            reader.MoveNext();
            value = new JamlStandardCardSlot();
            return true;
        }

        value = new JamlStandardCardSlot
        {
            Card = (MotelyStandardCard)nestedObjectDeserializer(reader, typeof(MotelyStandardCard))!,
        };
        return true;
    }
}
