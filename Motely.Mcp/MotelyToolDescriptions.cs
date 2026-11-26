namespace Motely.Mcp;

/// <summary>
/// Bot-facing tool descriptions. Keep the first three aligned with Motely.Web/src/webmcp-tools.ts:
/// an agent that learns the WebMCP surface can use this server without relearning the words.
/// Every description says WHEN to call it, WHEN NOT to, an example, and the cost.
/// </summary>
internal static class MotelyToolDescriptions
{
    public const string SeedValidate =
        "Validate a JAML seed filter: check that the text loads and builds a search plan. " +
        "WHEN: you have filter text (paste it, or name one in this repo's Filters folder) and want " +
        "to confirm it is well-formed before spending a search on it — or to get the line-numbered " +
        "reason it is not. DO-NOT-USE-FOR: finding seeds — that is seed_search's job; validating " +
        "never runs the engine. Example: { filter: \"name: F\\ndeck: Red\\nstake: White\\nmust:\\n" +
        "  - joker: Blueprint\\n    antes: [1]\" } → \"ok: F (Red White) — 1 must, 0 should, " +
        "0 mustNot; the search plan builds.\" Cost: negligible — a local parse, milliseconds.";

    public const string SeedSearch =
        "Search Balatro seeds against a JAML filter and return the matching seeds with the native " +
        "Motely engine. WHEN: the filter is valid (run seed_validate first if unsure) and you want " +
        "seeds that satisfy it. DO-NOT-USE-FOR: merely checking that a filter loads — use " +
        "seed_validate; it is cheaper. Modes: replay (default; the filter's own `seeds:` block), " +
        "seeds (comma-separated list in `seeds`), keyword (seeds containing `keyword`), random " +
        "(`randomCount` random seeds), sequential (sweep from the start of the ~2.3-trillion-seed " +
        "space — prefer random or keyword probes unless you mean an exhaustive sweep). Exactly one " +
        "selector must match `mode`. Example: { filter: \"…\", mode: \"seeds\", " +
        "seeds: \"GSNQDOAA,F6Z71ZAA\", collect: 10 } → \"F: 1 matched / 2 searched in 2ms " +
        "(1,000 seeds/s).\\nMatches: GSNQDOAA\". collect caps kept matches (default 100, max " +
        "10000). Cost: native SIMD speed; a loose filter or an unbounded sequential sweep can run " +
        "effectively forever — keep collect modest.";

    public const string SoulOrder =
        "Check a seed's Soul order: the run-global sequence of legendary faces the `Joker4` stream " +
        "gives to each Soul opened. The FACE is ANTELESS — ante only decides where the Soul card is " +
        "and which edition it has. WHILE reading it, remember: Soul #1, #2, #3 are positions in the " +
        "whole run, not antes. WHEN: you want to know whether a seed has Perkeo then Perkeo, or " +
        "which legendary a late Soul really is. DO-NOT-USE-FOR: searching for seeds — use " +
        "seed_search. Example: { seed: JNN5KG71, deck: Ghost, stake: White } → " +
        "\"soul order for JNN5KG71 (Ghost White): Perkeo → Chicot → Canio\". " +
        "Pass heldJokers to model `with: { jokers: [...] }` (Showman keeps duplicates). " +
        "maxAnte defaults to 39 — the whole Balatro run; lower it only for a quick peek. " +
        "Cost: one Jamlyzer walk, typically under a millisecond.";

    public const string BestSeed =
        "Rank a pile of seeds against a JAML filter and return the best one to play. WHEN: you have " +
        "a seed list (a plain file, motely CSV, or a comma-separated list) and want the highest " +
        "should-score seed — e.g. 46k seeds against longCola. DO-NOT-USE-FOR: checking that a filter " +
        "loads — use seed_validate; or finding new seeds outside your list — use seed_search. The " +
        "filter's own cutoff is ignored here so every listed seed can still be ranked. Example: " +
        "{ filter: longCola, seeds: mySeeds.txt, top: 10 } → \"best seed for simpleCola from " +
        "46,000 listed seeds: ABC12345, score 130\". `top` caps the returned table (default 10, " +
        "max 100). Cost: replaying a list takes milliseconds to seconds; the answer is the top row.";

    public const string AnalyzeSeed =
        "Inspect one seed like Blueprint's seed view: antes, bosses, vouchers, tags, shop cards, " +
        "packs, and the anteless Soul order. WHEN: you want to understand one seed, check where a " +
        "Soul card or edition sits, or debug why a filter matched. DO-NOT-USE-FOR: finding seeds " +
        "across a space — use seed_search. Pass `filter` to use the EXACT JAML you are searching " +
        "with: its antes, first written `with:` block and shop slots drive the window. Without `filter`, " +
        "`maxAnte` and `shopLimit` set a simple window. Example: " +
        "{ seed: GSNQDOAA, filter: \"name: ColaLit\\n…\" } → a compact JSON overview plus text. " +
        "Cost: one Jamlyzer walk; larger maxAnte/shopLimit mean more text.";
}
