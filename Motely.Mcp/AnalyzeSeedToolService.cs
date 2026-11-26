using System.Text.Json;
using Motely.Analysis;
using Motely.Filters;
using ModelContextProtocol.Protocol;

namespace Motely.Mcp;

/// <summary>
/// Blueprint-style seed inspection on Motely's Jamlyzer. When a JAML filter is passed, the SAME
/// filter drives the window: its antes, shop slots and `with:` block. Without one, maxAnte and
/// shopLimit give a simple overview.
/// </summary>
internal static class AnalyzeSeedToolService
{
    public static CallToolResult Execute(
        string? seed,
        string? filter,
        string? deck,
        string? stake,
        int maxAnte,
        int shopLimit
    )
    {
        if (!CliSearchMode.TryNormalizeSeed(seed ?? string.Empty, out string? canonicalSeed))
            return ToolResults.Error($"Error: seed is not a Balatro seed ({CliSearchMode.SeedRule}).");
        if (maxAnte is < 1 or > MotelyGlobals.MaxAnte)
            return ToolResults.Error($"Error: maxAnte must be between 1 and {MotelyGlobals.MaxAnte}.");
        if (shopLimit is < 1 or > 100)
            return ToolResults.Error("Error: shopLimit must be between 1 and 100.");

        JamlConfig? config;
        string? filterLabel = null;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (!FilterToolService.TryLoad(filter, out var loaded, out var error))
                return ToolResults.Error(error!);
            config = loaded!.Config;
            filterLabel = loaded.Label;
            config.Seeds.Clear();
            config.Seeds.Add(canonicalSeed!);
        }
        else
        {
            var windowClause = new Dictionary<string, object?>
            {
                ["soulOrder"] = new[] { "Perkeo" },
                ["maxAnte"] = maxAnte,
            };
            var document = new Dictionary<string, object?>
            {
                ["name"] = "seed-overview",
                ["deck"] = string.IsNullOrWhiteSpace(deck) ? "Red" : deck!.Trim(),
                ["stake"] = string.IsNullOrWhiteSpace(stake) ? "White" : stake!.Trim(),
                ["must"] = new[] { windowClause },
                ["seeds"] = new[] { canonicalSeed },
            };
            if (!JamlConfig.TryLoad(JsonSerializer.Serialize(document), out config, out var loadError))
                return ToolResults.Error(
                    string.IsNullOrWhiteSpace(loadError)
                        ? "Error: deck or stake did not load."
                        : $"Error: deck or stake did not load: {loadError}"
                );
        }

        try
        {
            var result = MotelyJamlyzer.Analyze(
                config!,
                eventRolls: 0,
                shopSlots: shopLimit,
                with: JamlConfigWith.Find(config!)
            ).FirstOrDefault();
            if (result is null)
                return ToolResults.Error("Error: the seed could not be analyzed.");

            string[] faces = result.LegendaryOrder.Select(static entry => entry.Joker.ToString()).ToArray();
            var antes = result.Antes.Select(static ante => new
            {
                ante = ante.Ante,
                boss = ante.Boss.ToString(),
                voucher = ante.Voucher.ToString(),
                smallBlindTag = ante.SmallBlindTag.ToString(),
                bigBlindTag = ante.BigBlindTag.ToString(),
                shop = ante.ShopItems.Select(static item => item.ToString()).ToArray(),
                packs = ante.Packs.Select(static pack => new
                {
                    pack = pack.Pack.ToString(),
                    cards = pack.Items.Select(static item => item.ToString()).ToArray(),
                }).ToArray(),
            }).ToArray();

            string head =
                $"{result.Seed} ({config!.Deck} {config.Stake}): {antes.Length} antes, " +
                $"{faces.Length} Souls — anteless soul order: {McpFormat.JoinArrows(faces)}.";
            string preview = string.Join(
                "\n",
                result.Antes.Take(6).Select(static ante =>
                    $"ante {ante.Ante}: {ante.Boss}, {ante.Voucher}; " +
                    $"shop: {string.Join(", ", ante.ShopItems.Take(6).Select(static item => item.ToString()))}"
                )
            );
            string text = preview.Length == 0 ? head : $"{head}\n{preview}";

            return ToolResults.Ok(
                text,
                new
                {
                    ok = true,
                    seed = result.Seed,
                    deck = config.Deck.ToString(),
                    stake = config.Stake.ToString(),
                    filter = filterLabel,
                    antelessSoulOrder = faces,
                    antes,
                }
            );
        }
        catch (Exception ex)
        {
            return ToolResults.Error($"Error: the seed analysis failed: {ex.GetBaseException().Message}");
        }
    }
}
