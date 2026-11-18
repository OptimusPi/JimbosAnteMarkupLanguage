using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Motely.Filters;

/// <summary>
/// Clause slots are <see cref="MotelyClause"/>. The first key that names a clause (<c>joker:</c>,
/// <c>tag:</c>, <c>or:</c>) picks the clause class, and the mapping is read again as that class.
/// YamlDotNet's own key discriminator cannot be added to the static builder, so this does that one job.
/// </summary>
internal sealed class ClauseKeyDeserializer : INodeDeserializer
{
    internal const string NeedsKey = "a clause needs a key such as joker:, rareJoker:, voucher:, boss:, and: or or:.";

    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer
    )
    {
        value = null;
        if (expectedType != typeof(MotelyClause))
            return false;
        // A scalar or a list in a clause slot is a clause that is not a mapping.
        if (reader.Current is not MappingStart start)
        {
            var at = reader.Current ?? throw new InvalidOperationException(NeedsKey);
            throw new YamlException(at.Start, at.End, NeedsKey);
        }

        var buffer = new ParserBuffer(reader, maxDepth: 64, maxLength: 100_000);
        buffer.Consume<MappingStart>();
        Type? clauseType = null;
        while (clauseType is null && !buffer.TryConsume<MappingEnd>(out _))
        {
            if (buffer.TryConsume<Scalar>(out var key))
                JamlClauseBinding.ClauseTypeByKey.TryGetValue(key.Value, out clauseType);
            else
                buffer.SkipThisAndNestedEvents();
            buffer.SkipThisAndNestedEvents();
        }
        if (clauseType is null)
            throw new YamlException(start.Start, start.End, NeedsKey);

        buffer.Reset();
        value = nestedObjectDeserializer(buffer, clauseType);
        return true;
    }
}
