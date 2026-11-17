using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely;

/// <summary>The Vecc generator fills this in at build time: no reflection, NativeAOT-safe.</summary>
[YamlStaticContext]
public partial class MotelyYamlContext : StaticContext;

/// <summary>A JAML (Jimbo's Ante Markup Language) document: the run, and the clauses a seed is held to.</summary>
[YamlSerializable]
public sealed partial class MotelyRunConfig
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public MotelyDeck Deck { get; set; }
    public MotelyStake Stake { get; set; }
    public int? Cutoff { get; set; }
    public List<string> Seeds { get; set; } = [];
    // YDNG001 asks for IMotelyClause to be registered, but the generator only registers classes and
    // structs, so an interface never can be. Every concrete clause is [YamlSerializable], and the
    // discriminator below picks one of them for each list item.
#pragma warning disable YDNG001
    public List<IMotelyClause> Must { get; set; } = [];
    public List<IMotelyClause> Should { get; set; } = [];
    public List<IMotelyClause> MustNot { get; set; } = [];
#pragma warning restore YDNG001

    // A clause is a mapping named by its one key (`- joker: Blueprint`, `- or: [...]`);
    // JamlClauseNodeDeserializer reads that key and picks the clause type from JamlClauseBinding.
    private static readonly IDeserializer Deserializer = JamlClauseBinding
        .Converters.Aggregate(
            new StaticDeserializerBuilder(new MotelyYamlContext())
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithCaseInsensitivePropertyMatching()
                .WithDuplicateKeyChecking()
                .WithNodeDeserializer(new JamlClauseNodeDeserializer())
                .WithNodeDeserializer(new JamlStandardCardSlotScalarDeserializer()),
            static (builder, converter) => builder.WithTypeConverter(converter)
        )
        .Build();

    public static MotelyRunConfig FromYaml(string yaml) =>
        Deserializer.Deserialize<MotelyRunConfig>(yaml) ?? new();

    public static MotelyRunConfig FromFile(string path) => FromYaml(File.ReadAllText(path));
}
