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
    public MotelyDeck Deck { get; set; }
    public MotelyStake Stake { get; set; }
    public int? Cutoff { get; set; }
    public List<string> Seeds { get; set; } = [];
    public List<IMotelyClause> Must { get; set; } = [];
    public List<IMotelyClause> Should { get; set; } = [];
    public List<IMotelyClause> MustNot { get; set; } = [];

    public static MotelyRunConfig FromYaml(string yaml) =>
        new StaticDeserializerBuilder(new MotelyYamlContext())
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Deserialize<MotelyRunConfig>(yaml) ?? new();

    public static MotelyRunConfig FromFile(string path) => FromYaml(File.ReadAllText(path));
}
