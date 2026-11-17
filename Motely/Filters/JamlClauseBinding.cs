using System.Collections.Frozen;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
namespace Motely.Filters;

/// <summary>JAML clause discriminators and scalar-or-list converters.</summary>
internal static class JamlClauseBinding
{
    internal static IReadOnlyCollection<string> ClauseKeys => DiscriminatorKeys;

    /// <summary>A name list written as one name (<c>joker: Blueprint</c>) or as a list; a number list likewise.</summary>
    internal static IEnumerable<IYamlTypeConverter> Converters =>
    [
        new ScalarOrSequenceConverter<MotelyJoker>(),
        new ScalarOrSequenceConverter<MotelyJokerCommon>(),
        new ScalarOrSequenceConverter<MotelyJokerUncommon>(),
        new ScalarOrSequenceConverter<MotelyJokerRare>(),
        new ScalarOrSequenceConverter<MotelyJokerSticker>(),
        new ScalarOrSequenceConverter<MotelyPlanetCard>(),
        new ScalarOrSequenceConverter<MotelySpectralCard>(),
        new ScalarOrSequenceConverter<MotelyTarotCard>(),
        new ScalarOrSequenceConverter<MotelyVoucher>(),
        new ScalarOrSequenceConverter<MotelyTag>(),
        new ScalarOrSequenceConverter<MotelyBossBlind>(),
        new ScalarOrSequenceConverter<MotelyBoosterPack>(),
        new ScalarOrSequenceConverter<MotelyPokerHand>(),
        new IntListConverter(),
    ];

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

    /// <summary>
    /// A name list: one name (<c>voucher: Telescope</c>), a list (<c>[Telescope, Observatory]</c>), or
    /// an any-word (<c>legendaryJoker: any</c>), which is the empty list: no name pinned.
    /// </summary>
    internal sealed class ScalarOrSequenceConverter<T> : IYamlTypeConverter
        where T : struct, Enum
    {
        public bool Accepts(Type type) => type == typeof(T[]);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.TryConsume<Scalar>(out var scalar))
                return IsAnyWord(scalar.Value) ? Array.Empty<T>() : new[] { Name(scalar) };

            parser.Consume<SequenceStart>();
            var items = new List<T>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                items.Add(Name(parser.Consume<Scalar>()));
            return items.ToArray();
        }

        private static T Name(Scalar scalar)
        {
            try
            {
                return JamlEnumName.Parse<T>(scalar.Value);
            }
            catch (FormatException ex)
            {
                throw new YamlException(scalar.Start, scalar.End, ex.Message, ex);
            }
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
            serializer(value, type);
    }

    /// <summary>
    /// A number list (<c>antes</c>, <c>shopItems</c>, …): a list, or one number for a list of one
    /// (<c>ante: 2</c> is <c>[2]</c>). Each number runs from 0 to <see cref="MotelyGlobals.MaxRollIndex"/>.
    /// A null value stays null, so the property keeps its default.
    /// </summary>
    internal sealed class IntListConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(int[]);

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.Current is Scalar { Style: ScalarStyle.Plain, Value: "" or "~" or "null" or "Null" or "NULL" })
            {
                parser.MoveNext();
                return null;
            }

            if (!parser.TryConsume<SequenceStart>(out _))
                return new[] { Number(parser, nested) };

            var items = new List<int>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                items.Add(Number(parser, nested));
            return items.ToArray();
        }

        private static int Number(IParser parser, ObjectDeserializer nested)
        {
            if (parser.Current is not Scalar scalar)
            {
                var at = parser.Current!;
                throw new YamlException(at.Start, at.End, "Write a whole number, or a list of them, as in [1, 2].");
            }

            var value = (int)nested(typeof(int))!;
            if (value is < 0 or > MotelyGlobals.MaxRollIndex)
                throw new YamlException(
                    scalar.Start,
                    scalar.End,
                    $"`{value}` is out of range: a number in this list runs from 0 to {MotelyGlobals.MaxRollIndex}."
                );
            return value;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
            serializer(value, type);
    }
}
