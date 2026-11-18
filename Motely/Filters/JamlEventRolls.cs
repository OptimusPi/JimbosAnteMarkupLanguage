using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>Event-clause roll shorthand: <c>luckyMoney: 1</c>, <c>luckyMoney: []</c>, or a list.</summary>
[YamlSerializable]
public sealed class JamlEventRolls
{
    public int[] Rolls { get; set; } = [];

    public int[] ToArray() => Rolls;

    internal sealed class Converter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(JamlEventRolls);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.Accept<Scalar>(out var scalar) && JamlClauseBinding.IsCategoryAny(scalar))
            {
                parser.MoveNext();
                return new JamlEventRolls();
            }

            try
            {
                return new JamlEventRolls { Rolls = (int[]?)nested(typeof(int[])) ?? [] };
            }
            catch (YamlException ex) when (ex.Message.Contains("a number in this list", StringComparison.Ordinal))
            {
                throw new YamlException(ex.Start, ex.End, ex.Message.Replace("a number in this list", "a roll", StringComparison.Ordinal));
            }
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            throw new NotSupportedException();
    }
}
