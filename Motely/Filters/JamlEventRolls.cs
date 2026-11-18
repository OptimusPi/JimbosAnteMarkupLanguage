using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>Event-clause rolls: <c>luckyMoney: 1</c> or a list such as <c>luckyMoney: [1, 2]</c>. At least one roll.</summary>
[YamlSerializable]
public sealed class JamlEventRolls
{
    public required int[] Rolls { get; set; }

    public int[] ToArray() => Rolls;

    internal sealed class Converter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(JamlEventRolls);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            var start = parser.Current?.Start ?? Mark.Empty;
            var end = parser.Current?.End ?? Mark.Empty;
            int[] rolls = parser.Current switch
            {
                SequenceStart => ReadIntSequence(parser),
                Scalar scalar when scalar.Value.Trim().Length > 0 => [ReadRoll(parser.Consume<Scalar>())],
                _ => [],
            };
            if (rolls.Length == 0)
                throw new YamlException(start, end, "an event clause needs at least one roll: write a whole number, or a list of them, as in [1, 2].");
            return new JamlEventRolls { Rolls = rolls };
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            nested(value, type);

        private static int[] ReadIntSequence(IParser parser)
        {
            parser.Consume<SequenceStart>();
            var items = new List<int>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                items.Add(ReadRoll(parser.Consume<Scalar>()));

            return items.ToArray();
        }

        private static int ReadRoll(Scalar scalar)
        {
            if (!int.TryParse(scalar.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var roll))
                throw new YamlException(scalar.Start, scalar.End, $"`{scalar.Value}` is not a roll: write a whole number, or a list of them, as in [1, 2].");
            if (roll is < 0 or > MotelyGlobals.MaxRollIndex)
                throw new YamlException(scalar.Start, scalar.End, $"`{roll}` is out of range: a roll runs from 0 to {MotelyGlobals.MaxRollIndex}.");
            return roll;
        }
    }
}
