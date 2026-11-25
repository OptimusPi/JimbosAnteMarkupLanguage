using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
namespace Motely.Filters;

/// <summary>JAML clause discriminators and scalar-or-list converters.</summary>
internal static class JamlClauseBinding
{
    internal static IReadOnlyCollection<string> ClauseKeys => ClauseTypeByKey.Keys;

    internal static IEnumerable<IYamlTypeConverter> Converters =>
    [
        new ScalarOrSequenceConverter<MotelyJoker>(),
        new ScalarOrSequenceConverter<MotelyJokerCommon>(),
        new ScalarOrSequenceConverter<MotelyJokerUncommon>(),
        new ScalarOrSequenceConverter<MotelyJokerRare>(),
        new ScalarOrSequenceConverter<MotelyJokerLegendary>(),
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
        new JamlEventRolls.Converter(),
        new SoulOrderEntryListConverter(),
    ];

    internal static readonly Dictionary<string, Type> ClauseTypeByKey =
        new(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Scalar, empty, <c>any</c>, or sequence → array (Nat's JAML filter clauses).</summary>
    internal sealed class ScalarOrSequenceConverter<T> : IYamlTypeConverter
        where T : struct, Enum
    {
        public bool Accepts(Type type) => type == typeof(T[]);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.Accept<Scalar>(out var scalar) && IsCategoryAny(scalar))
            {
                parser.MoveNext();
                return Array.Empty<T>();
            }

            if (!parser.TryConsume<SequenceStart>(out _))
                return new[] { (T)nested(typeof(T))! };

            var items = new List<T>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                items.Add((T)nested(typeof(T))!);
            return items.ToArray();
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A list of numbers (<c>antes</c>, <c>shopItems</c>, …) as a list, or as one number for a list
    /// of one: <c>ante: 2</c> is <c>antes: [2]</c>. Each number reads as YamlDotNet reads an int; a
    /// null value stays null here, and the property it lands on keeps its default (<c>ante: ~</c>,
    /// a <c>sources:</c> slot) or the loader refuses it (<c>antes: ~</c>, <c>rolls: ~</c>).
    /// </summary>
    internal sealed class IntListConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(int[]);

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.Current is Scalar scalar && scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL")
            {
                parser.MoveNext();
                return null;
            }
            if (parser.Current is not SequenceStart)
                return new[] { Number(parser, nested) };

            parser.Consume<SequenceStart>();
            var items = new List<int>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                items.Add(Number(parser, nested));
            return items.ToArray();
        }

        private static int Number(IParser parser, ObjectDeserializer nested)
        {
            if (parser.Current is not Scalar scalar)
            {
                var ev = parser.Current ?? throw new FormatException(
                    "a nested list is not a number: write a whole number, or a list of them, as in [1, 2].");
                throw new YamlException(
                    ev.Start,
                    ev.End,
                    "a nested list is not a number: write a whole number, or a list of them, as in [1, 2]."
                );
            }

            var text = scalar.Value;
            int value;
            try
            {
                value = (int)nested(typeof(int))!;
            }
            catch (Exception ex) when (text is not null && ex is FormatException or OverflowException or YamlException)
            {
                throw new FormatException($"'{text}' is not a number: write a whole number, or a list of them, as in [1, 2].", ex);
            }
            if (JamlConfigLoader.ListNumber.BrokenBy(value))
                throw new YamlException(scalar.Start, scalar.End, $"`{value}` is out of range: a number in this list runs from 0 to {MotelyGlobals.MaxRollIndex}.");
            return value;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            throw new NotSupportedException();
    }

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

    internal static bool IsCategoryAny(Scalar scalar) =>
        scalar.Style is ScalarStyle.Plain or ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted
        && IsAnyWord(scalar.Value);
}

/// <summary>
/// Lets a <c>soulOrder:</c> entry be written as a bare legendary name (<c>- Perkeo</c>) instead of
/// the full mapping (<c>- { legendary: Perkeo }</c>). Antes default to "any ante"; add <c>antes:</c>
/// only to constrain where that Soul may open. The point of soulOrder is the *order* of legendaries;
/// location is optional and usually already pinned by standalone legendaryJoker clauses.
/// </summary>
internal sealed class SoulOrderEntryListConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(SoulOrderEntry[]);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
    {
        var entries = new List<SoulOrderEntry>();

        if (parser.Current is Scalar scalar)
        {
            // A null/empty/"any" value is no entry: an empty list, which the clause rejects.
            if (JamlClauseBinding.IsAnyWord(scalar.Value ?? ""))
            {
                parser.MoveNext();
                return Array.Empty<SoulOrderEntry>();
            }

            var joker = (MotelyJoker)nested(typeof(MotelyJoker))!;
            entries.Add(new SoulOrderEntry { Legendary = joker });
            return entries.ToArray();
        }

        if (parser.Current is not SequenceStart)
            return entries.ToArray();

        parser.Consume<SequenceStart>();
        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            if (parser.Current is Scalar)
            {
                var joker = (MotelyJoker)nested(typeof(MotelyJoker))!;
                entries.Add(new SoulOrderEntry { Legendary = joker });
                continue;
            }

            if (parser.Current is MappingStart)
            {
                parser.Consume<MappingStart>();
                var entry = new SoulOrderEntry();
                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    if (!parser.TryConsume<Scalar>(out var key))
                    {
                        parser.SkipThisAndNestedEvents();
                        continue;
                    }
                    if (key.Value.Equals("legendary", StringComparison.OrdinalIgnoreCase)
                        || key.Value.Equals("legendaryJoker", StringComparison.OrdinalIgnoreCase)
                        || key.Value.Equals("soulJoker", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Legendary = (MotelyJoker)nested(typeof(MotelyJoker))!;
                    }
                    else if (key.Value.Equals("antes", StringComparison.OrdinalIgnoreCase)
                             || key.Value.Equals("ante", StringComparison.OrdinalIgnoreCase))
                    {
                        var antes = (int[]?)nested(typeof(int[]));
                        entry.Antes = antes ?? [];
                    }
                    else
                    {
                        parser.SkipThisAndNestedEvents();
                    }
                }
                entries.Add(entry);
                continue;
            }

            parser.SkipThisAndNestedEvents();
        }

        return entries.ToArray();
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
        throw new NotSupportedException();
}
