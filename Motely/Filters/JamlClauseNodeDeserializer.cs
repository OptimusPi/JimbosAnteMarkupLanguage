using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.BufferedDeserialization;
using YamlDotNet.Serialization.BufferedDeserialization.TypeDiscriminators;

namespace Motely.Filters;

/// <summary>
/// Reads one JAML clause (<c>- joker: Blueprint</c>, <c>- or: [...]</c>) into its concrete
/// <see cref="IMotelyClause"/> type. The clause's key names the type (<see cref="JamlClauseBinding"/>);
/// the deserializer's own pipeline, and so the Vecc-generated static code, builds the object.
/// </summary>
/// <remarks>
/// YamlDotNet's <c>StaticDeserializerBuilder.WithTypeDiscriminatingNodeDeserializer</c> (18.1.0)
/// registers itself before <c>DictionaryNodeDeserializer</c>, which a static builder never has, so the
/// builder throws on Build. This does the same job with the same public pieces.
/// </remarks>
internal sealed class JamlClauseNodeDeserializer : INodeDeserializer
{
    private readonly UniqueKeyTypeDiscriminator _discriminator =
        new(typeof(IMotelyClause), JamlClauseBinding.ClauseTypeByKey);

    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer
    )
    {
        value = null;
        if (expectedType != typeof(IMotelyClause) || !reader.Accept<MappingStart>(out var mapping))
            return false;

        var buffer = new ParserBuffer(reader, maxDepth: -1, maxLength: -1);
        if (!_discriminator.TryDiscriminate(buffer, out var clauseType) || clauseType is null)
            throw new YamlException(
                mapping.Start,
                mapping.End,
                $"Not a clause: it needs one of these keys: {string.Join(", ", JamlClauseBinding.ClauseKeys.Order())}."
            );

        buffer.Reset();
        value = nestedObjectDeserializer(buffer, clauseType);
        return true;
    }
}
