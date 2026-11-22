using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Motely.Analysis;
using Motely.Filters;

namespace Motely.Mcp;

/// <summary>Where a filter would not load: the engine's own message, and its line when it gave one.</summary>
public sealed record JamlLoadError(string Message, int? Line, int? Column);

/// <summary>One clause, named by where it sits in the filter (<c>must[2]</c>) and in words.</summary>
public sealed record ClauseRef(string Path, string Label);

/// <summary>One should clause's raw count for a seed.</summary>
public sealed record TallyValue(string Label, int Count);

/// <summary>
/// A seed checked clause by clause. <c>Pass</c> is every must matched and no mustNot matched;
/// <c>FailedMust</c> and <c>MustNotHit</c> say which clause stopped it.
/// </summary>
public sealed record SeedVerdict(
    string Seed,
    bool Pass,
    int? Score,
    IReadOnlyList<TallyValue>? Tallies,
    IReadOnlyList<ClauseRef>? FailedMust,
    IReadOnlyList<ClauseRef>? MustNotHit,
    string? Error
);

/// <summary>
/// A clause written without <c>sources:</c>, and the sources the engine read for it instead.
/// <c>NotCounted</c> lists the source kinds the default leaves out (booster packs, for a joker).
/// </summary>
public sealed record DefaultSourceUse(string Path, string Label, string Kind, JsonNode Sources, IReadOnlyList<string> NotCounted);

/// <summary>
/// The engine, as the MCP tools use it. Every answer comes from Motely itself: the JAML loader,
/// the search settings builder, <see cref="ClauseScoring"/> and the Jamlyzer.
/// </summary>
public static class McpEngine
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    public static int DefaultThreads => Math.Max(1, Environment.ProcessorCount - 2);

    // ---------------------------------------------------------------- load

    /// <summary>
    /// Loads a filter through the engine's loader and builds its search settings, so a filter that
    /// loads but cannot be searched is refused here too.
    /// </summary>
    public static bool TryLoad(string jaml, out JamlConfig config, out JamlLoadError? error)
    {
        config = null!;
        error = null;
        try
        {
            config = JamlConfig.FromJaml(jaml ?? "");
            MotelySearchBuilder.CreateSettings(config);
            return true;
        }
        catch (JamlFilterException ex)
        {
            error = new JamlLoadError(ex.Message, ex.Line, ex.Column);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            error = new JamlLoadError(ex.Message, null, null);
        }
        return false;
    }

    public static string Label(MotelyClause clause, int index) => MotelySearchBuilder.DefaultTallyLabel(clause, index);

    // ---------------------------------------------------------------- score

    /// <summary>
    /// Checks each seed clause by clause on a scalar per-seed context (a passthrough search with a
    /// Jimmolate predicate). This path does not use the filter's SIMD chain, so it is an independent
    /// check of what a search reported. Every must, mustNot and should clause is counted through
    /// <see cref="ClauseScoring"/>, the same counts the scoring pass uses.
    /// </summary>
    public static IReadOnlyList<SeedVerdict> ScoreSeeds(JamlConfig config, IReadOnlyList<string> seeds, int? threads = null)
    {
        var must = config.Must.ToArray();
        var should = config.Should.ToArray();
        var mustNot = config.MustNot.ToArray();
        MotelyClause[] all = [.. must, .. should, .. mustNot];
        var mustLabels = must.Select((c, i) => new ClauseRef($"must[{i}]", Label(c, i))).ToArray();
        var mustNotLabels = mustNot.Select((c, i) => new ClauseRef($"mustNot[{i}]", Label(c, i))).ToArray();
        var shouldLabels = should.Select((c, i) => Label(c, i)).ToArray();

        var normalized = new string?[seeds.Count];
        var valid = new List<string>();
        for (int i = 0; i < seeds.Count; i++)
        {
            if (CliSearchMode.TryNormalizeSeed(seeds[i] ?? "", out var seed))
            {
                normalized[i] = seed;
                valid.Add(seed);
            }
        }
        var distinct = valid.Distinct().ToArray();
        var results = new ConcurrentDictionary<string, SeedVerdict>();

        if (distinct.Length > 0)
        {
            int threadCount = Math.Clamp(threads ?? distinct.Length / 4096 + 1, 1, DefaultThreads);
            var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(new PassthroughFilterDesc())
                .WithDeck(config.Deck)
                .WithStake(config.Stake)
                .WithSeedList(distinct)
                .WithThreadCount(threadCount)
                .WithJimmolate(
                    ctx =>
                    {
                        string seed = ctx.GetSeed();
                        results[seed] = Check(ref ctx, seed, must, should, mustNot, all, mustLabels, mustNotLabels, shouldLabels);
                        return 0;
                    }
                );
            using var search = settings.Start();
            search.AwaitCompletion();
        }

        var verdicts = new SeedVerdict[seeds.Count];
        for (int i = 0; i < seeds.Count; i++)
        {
            verdicts[i] = normalized[i] is { } seed && results.TryGetValue(seed, out var verdict)
                ? verdict
                : new SeedVerdict(seeds[i] ?? "", false, null, null, null, null,
                    normalized[i] is null
                        ? $"not a seed ({CliSearchMode.SeedRule})"
                        : "the engine did not visit this seed");
        }
        return verdicts;
    }

    private static SeedVerdict Check(
        ref MotelySingleSearchContext ctx,
        string seed,
        MotelyClause[] must,
        MotelyClause[] should,
        MotelyClause[] mustNot,
        MotelyClause[] all,
        ClauseRef[] mustLabels,
        ClauseRef[] mustNotLabels,
        string[] shouldLabels
    )
    {
        var runState = ctx.Deck.GetDefaultRunState();
        if (all.Length > 0)
            ClauseScoring.PrepareRunState(ref ctx, all, runState);

        List<ClauseRef>? failed = null;
        for (int i = 0; i < must.Length; i++)
        {
            int raw = ClauseScoring.CountRawOccurrences(ref ctx, must[i], runState);
            if (!ClauseScoring.Matches(raw, must[i]))
                (failed ??= []).Add(mustLabels[i]);
        }
        List<ClauseRef>? hit = null;
        for (int i = 0; i < mustNot.Length; i++)
        {
            int raw = ClauseScoring.CountRawOccurrences(ref ctx, mustNot[i], runState);
            if (ClauseScoring.Matches(raw, mustNot[i]))
                (hit ??= []).Add(mustNotLabels[i]);
        }
        if (failed is not null || hit is not null)
            return new SeedVerdict(seed, false, null, null, failed, hit, null);

        // The scoring pass's own arithmetic (JamlShouldScoreDesc): raw count in the tally, paid
        // from min up, capped at max; an and:/or: weighs its arms with CountOccurrences.
        long total = 0;
        var tallies = new TallyValue[should.Length];
        for (int i = 0; i < should.Length; i++)
        {
            int raw = ClauseScoring.CountRawOccurrences(ref ctx, should[i], runState);
            tallies[i] = new TallyValue(shouldLabels[i], raw);
            if (should[i] is not LogicClause && raw < should[i].Min)
                continue;
            int weighted = should[i] is LogicClause
                ? ClauseScoring.CountOccurrences(ref ctx, should[i], runState)
                : ClauseScoring.CapScoreCount(raw, should[i]);
            total += (long)weighted * should[i].Score;
        }
        return new SeedVerdict(seed, true, (int)Math.Clamp(total, int.MinValue, int.MaxValue), tallies, null, null, null);
    }

    // ---------------------------------------------------------------- default sources

    /// <summary>Every clause written without <c>sources:</c>, with the sources the engine read instead.</summary>
    public static List<DefaultSourceUse> DefaultSourcesUsed(JamlConfig config)
    {
        var uses = new List<DefaultSourceUse>();
        Walk(config.Must, "must");
        Walk(config.Should, "should");
        Walk(config.MustNot, "mustNot");
        return uses;

        void Walk(IReadOnlyList<MotelyClause> clauses, string prefix)
        {
            for (int i = 0; i < clauses.Count; i++)
                Visit(clauses[i], $"{prefix}[{i}]", i);
        }

        void Visit(MotelyClause clause, string path, int index)
        {
            string label = Label(clause, index);
            switch (clause)
            {
                case LogicClause logic:
                    for (int j = 0; j < logic.Clauses.Length; j++)
                        Visit(logic.Clauses[j], $"{path}.clauses[{j}]", j);
                    break;
                case JokerClause c:
                    bool anyNonLegendary = c.Jokers.Length == 0 || c.Jokers.Any(j => !IsLegendary(j));
                    if (c.Sources is null && anyNonLegendary)
                        Add(path, label, "joker", JokerFilterDesc.DefaultSources);
                    if (c.LegendarySources is null && JokerFilterDesc.UsesLegendaryPath(c))
                        Add(path, label, "legendaryJoker", LegendaryJokerFilterDesc.DefaultSources);
                    break;
                case CommonJokerClause { Sources: null }:
                    Add(path, label, "commonJoker", CommonJokerFilterDesc.DefaultSources);
                    break;
                case UncommonJokerClause { Sources: null }:
                    Add(path, label, "uncommonJoker", UncommonJokerFilterDesc.DefaultSources);
                    break;
                case RareJokerClause { Sources: null }:
                    Add(path, label, "rareJoker", RareJokerFilterDesc.DefaultSources);
                    break;
                case LegendaryJokerClause { Sources: null }:
                    Add(path, label, "legendaryJoker", LegendaryJokerFilterDesc.DefaultSources);
                    break;
                case TarotCardClause { Sources: null }:
                    Add(path, label, "tarotCard", TarotCardFilterDesc.DefaultSources);
                    break;
                case PlanetCardClause { Sources: null }:
                    Add(path, label, "planetCard", PlanetCardFilterDesc.DefaultSources);
                    break;
                case SpectralCardClause { Sources: null }:
                    Add(path, label, "spectralCard", SpectralCardFilterDesc.DefaultSources);
                    break;
                case StandardCardClause { Sources: null }:
                    Add(path, label, "standardCard", StandardCardFilterDesc.DefaultSources);
                    break;
            }
        }

        void Add(string path, string label, string kind, object sources)
        {
            var all = JsonSerializer.SerializeToNode(sources, sources.GetType(), Json)!.AsObject();
            var used = new JsonObject();
            var notCounted = new List<string>();
            foreach (var (key, value) in all)
            {
                bool empty = value is null
                    || (value is JsonArray { Count: 0 })
                    || (value is JsonValue v && v.TryGetValue(out bool b) && !b);
                if (empty)
                    notCounted.Add(key);
                else
                    used[key] = value!.DeepClone();
            }
            uses.Add(new DefaultSourceUse(path, label, kind, used, notCounted));
        }
    }

    private static bool IsLegendary(MotelyJoker joker) =>
        (MotelyJokerRarity)((int)joker & MotelyGlobals.JokerRarityMask) == MotelyJokerRarity.Legendary;

    // ---------------------------------------------------------------- explain

    public static readonly string[] ExplainSections = ["boss", "voucher", "tags", "shop", "packs", "legendary"];

    /// <summary>
    /// What a seed holds per ante, read by the Jamlyzer walk: boss, voucher, both blind tags, the
    /// shop (with stickers and editions), every booster pack with its cards, and the Soul order.
    /// </summary>
    public static JsonObject ExplainSeed(string seed, MotelyDeck deck, MotelyStake stake, int[]? antes, string[]? include, int shopSlots = 0)
    {
        if (!CliSearchMode.TryNormalizeSeed(seed ?? "", out var canonical))
            throw new ArgumentException($"'{seed}' is not a seed ({CliSearchMode.SeedRule}).");
        int[] walk = [.. (antes is { Length: > 0 } ? antes : [1, 2, 3, 4, 5, 6, 7, 8]).Distinct().Order()];
        if (walk.Any(a => a is < 0 or > 39))
            throw new ArgumentException("antes run from 0 to 39.");
        var sections = include is { Length: > 0 } ? include.Select(s => s.Trim().ToLowerInvariant()).ToHashSet() : [.. ExplainSections];
        var unknown = sections.Except(ExplainSections).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"unknown include '{string.Join(", ", unknown)}'; known: {string.Join(", ", ExplainSections)}.");

        var desc = new MotelyJamlyzerFilterDesc(walk, eventRolls: 0, resumeFrom: null, shopSlots: shopSlots, opensAnteZeroSouls: false);
        using (var search = new MotelySearchSettings<MotelyJamlyzerFilterDesc.JamlyzerFilter>(desc)
            .WithDeck(deck).WithStake(stake).WithSeedList([canonical]).WithThreadCount(1).Start())
        {
            search.AwaitCompletion();
        }
        var result = desc.Result ?? throw new InvalidOperationException($"the engine did not walk seed {canonical}.");

        var anteRows = new JsonArray();
        // The walk passes through every ante up to the last one asked for; show only those asked for.
        foreach (var ante in result.Antes.Where(a => walk.Contains(a.Ante)))
        {
            var row = new JsonObject { ["ante"] = ante.Ante };
            if (sections.Contains("boss"))
                row["boss"] = ante.Boss.ToString();
            if (sections.Contains("voucher"))
                row["voucher"] = ante.Voucher.ToString();
            if (sections.Contains("tags"))
            {
                row["smallBlindTag"] = ante.SmallBlindTag.ToString();
                row["bigBlindTag"] = ante.BigBlindTag.ToString();
            }
            if (sections.Contains("shop"))
                row["shop"] = new JsonArray([.. ante.ShopItems.Select((item, slot) => (JsonNode)$"{slot}: {item}")]);
            if (sections.Contains("packs"))
                row["packs"] = new JsonArray([.. ante.Packs.Select((pack, slot) => (JsonNode)new JsonObject
                {
                    ["slot"] = slot,
                    ["pack"] = pack.Pack.ToString(),
                    ["cards"] = new JsonArray([.. pack.Items.Select(i => (JsonNode)i.ToString())]),
                })]);
            anteRows.Add(row);
        }

        var output = new JsonObject
        {
            ["seed"] = canonical,
            ["deck"] = deck.ToString(),
            ["stake"] = stake.ToString(),
            ["antes"] = anteRows,
        };
        if (sections.Contains("legendary"))
            output["legendaryOrder"] = new JsonArray([.. result.LegendaryOrder.Select(l => (JsonNode)new JsonObject
            {
                ["ante"] = l.Ante,
                ["packSlot"] = l.PackSlot,
                ["joker"] = l.Item.ToString(),
            })]);
        return output;
    }

    // ---------------------------------------------------------------- vocabulary

    public static readonly IReadOnlyDictionary<string, Type> VocabularyKinds = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["jokers"] = typeof(MotelyJoker),
        ["tags"] = typeof(MotelyTag),
        ["vouchers"] = typeof(MotelyVoucher),
        ["spectrals"] = typeof(MotelySpectralCard),
        ["tarots"] = typeof(MotelyTarotCard),
        ["planets"] = typeof(MotelyPlanetCard),
        ["bosses"] = typeof(MotelyBossBlind),
        ["stickers"] = typeof(MotelyJokerSticker),
        ["editions"] = typeof(MotelyItemEdition),
        ["decks"] = typeof(MotelyDeck),
        ["stakes"] = typeof(MotelyStake),
    };

    public static string[] Vocabulary(string kind) =>
        VocabularyKinds.TryGetValue(kind?.Trim() ?? "", out var type)
            ? Enum.GetNames(type)
            : throw new ArgumentException($"unknown kind '{kind}'; known: {string.Join(", ", VocabularyKinds.Keys)}.");
}
