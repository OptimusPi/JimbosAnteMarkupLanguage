namespace Motely.Filters;

/// <summary>Event-clause roll shorthand: <c>luckyMoney: 1</c>, <c>luckyMoney: []</c>, or a list.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed class JamlEventRolls
{
    public int[] Rolls { get; set; } = [];

    public int[] ToArray() => Rolls;
}
