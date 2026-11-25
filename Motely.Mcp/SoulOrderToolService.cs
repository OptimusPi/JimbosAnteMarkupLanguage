using System.Text.Json;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;
using ModelContextProtocol.Protocol;

namespace Motely.Mcp;

/// <summary>
/// The anteless Soul-order check. The Joker4 pool has no ante key: entry N is the Nth Soul opened
/// in the whole run. Only the Soul card's location and edition are ante-bound, so the tool reports
/// faces in order and never attaches an ante to a face.
/// </summary>
internal static class SoulOrderToolService
{
    public static CallToolResult Execute(
        string? seed,
        string? deck,
        string? stake,
        int maxAnte,
        string? heldJokers
    )
    {
        if (!CliSearchMode.TryNormalizeSeed(seed ?? string.Empty, out string? canonicalSeed))
            return ToolResults.Error(
                $"Error: seed is not a Balatro seed ({CliSearchMode.SeedRule})."
            );
        if (maxAnte is < 1 or > MotelyGlobals.MaxAnte)
            return ToolResults.Error($"Error: maxAnte must be between 1 and {MotelyGlobals.MaxAnte}.");

        string deckText = string.IsNullOrWhiteSpace(deck) ? "Red" : deck!.Trim();
        string stakeText = string.IsNullOrWhiteSpace(stake) ? "White" : stake!.Trim();
        string[] jokers = string.IsNullOrWhiteSpace(heldJokers)
            ? []
            : heldJokers!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Build a tiny JAML document with JSON (the loader accepts JSON) so names can never break
        // the document shape. soulOrder here only opens the Jamlyzer's full-run window; it is not a
        // filter clause the caller has to understand.
        var soulClause = new Dictionary<string, object?>
        {
            ["soulOrder"] = new[] { "Perkeo" },
            ["maxAnte"] = maxAnte,
        };
        if (jokers.Length > 0)
            soulClause["with"] = new Dictionary<string, object?> { ["jokers"] = jokers };
        var document = new Dictionary<string, object?>
        {
            ["name"] = "soul-order-check",
            ["deck"] = deckText,
            ["stake"] = stakeText,
            ["must"] = new[] { soulClause },
            ["seeds"] = new[] { canonicalSeed },
        };

        if (!JamlConfig.TryLoad(JsonSerializer.Serialize(document), out var config, out var loadError))
            return ToolResults.Error(
                string.IsNullOrWhiteSpace(loadError)
                    ? "Error: deck, stake or heldJokers did not load."
                    : $"Error: deck, stake or heldJokers did not load: {loadError}"
            );

        try
        {
            var result = MotelyJamlyzer.Analyze(config, eventRolls: 0).FirstOrDefault();
            if (result is null)
                return ToolResults.Error("Error: the seed could not be analyzed.");

            string[] faces = result.LegendaryOrder
                .Select(static entry => entry.Joker.ToString())
                .ToArray();
            bool backToBackPerkeo = faces.Length >= 2
                && faces[0] == nameof(MotelyJoker.Perkeo)
                && faces[1] == nameof(MotelyJoker.Perkeo);
            string deckName = config.Deck.ToString();
            string stakeName = config.Stake.ToString();
            string text =
                $"soul order for {result.Seed} ({deckName} {stakeName}): {McpFormat.JoinArrows(faces)} "
                + $"— Perkeo → Perkeo: {(backToBackPerkeo ? "yes" : "no")}.";

            return ToolResults.Ok(
                text,
                new
                {
                    ok = true,
                    seed = result.Seed,
                    deck = deckName,
                    stake = stakeName,
                    anteless = true,
                    count = faces.Length,
                    perkeoThenPerkeo = backToBackPerkeo,
                    order = faces.Select((joker, index) => new { index, joker }).ToArray(),
                    heldJokers = jokers,
                }
            );
        }
        catch (Exception ex)
        {
            return ToolResults.Error($"Error: the Soul order check failed: {ex.GetBaseException().Message}");
        }
    }
}
