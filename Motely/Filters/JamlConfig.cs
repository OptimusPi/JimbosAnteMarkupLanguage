namespace Motely.Filters;

/// <summary>
/// One filter document: who wrote it, which deck and stake, the seeds to replay, and the
/// must/should/mustNot clauses. Every default lives here in an initializer; the loader only
/// assigns keys the document actually wrote.
/// </summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record JamlConfig
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }

    /// <summary>When the filter was written, as its author's tool wrote it. Metadata: the search never reads it.</summary>
    public string? DateCreated { get; set; }
    public MotelyDeck Deck { get; set; } = MotelyDeck.Red;
    public MotelyStake Stake { get; set; } = MotelyStake.White;
    public List<string> Seeds { get; set; } = [];

    /// <summary>
    /// Fixed score floor: only seeds scoring at least this are reported. When set it replaces the
    /// auto (running-maximum) cutoff; null leaves the cutoff to the host (auto by default). An
    /// explicit host cutoff (the CLI's <c>--cutoff</c>) still wins over this.
    /// </summary>
    public int? Cutoff { get; set; }

    public List<MotelyClause> Must { get; set => field = value ?? []; } = [];
    public List<MotelyClause> Should { get; set => field = value ?? []; } = [];
    public List<MotelyClause> MustNot { get; set => field = value ?? []; } = [];

    public bool HasAnyClauses() => Must.Count != 0 || Should.Count != 0 || MustNot.Count != 0;

    public static bool TryLoad(
        string text,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JamlConfig? config,
        out string? error
    ) => JamlConfigLoader.TryLoad(text, out config, out error);

    public static JamlConfig FromFile(string path) => JamlConfigLoader.FromFile(path);

    public static JamlConfig FromJaml(string text) => JamlConfigLoader.FromJaml(text);

    public static string ToJson(string yaml) => JamlConfigLoader.ToJson(yaml);

    public static string ToYaml(string json) => JamlConfigLoader.ToYaml(json);
}
