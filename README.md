# Jimbo's Ante Markup Language (Motely)

Balatro seed search. You write a filter in JAML (YAML-shaped), and a SIMD engine sweeps seeds against it.

| Project | What it is |
|---|---|
| `Motely` | The engine and the JAML filter loader |
| `Motely.CLI` | The command-line search. The build names it `Motely.CLI`; the examples below call it `motely` |
| `Motely.Mcp` | A stdio MCP server for bots and agents: `seed_validate`, `seed_search`, `soul_order`, `best_seed`, `analyze_seed` |
| `Motely.Tests` | The test suite |
| `Motely.Wasm` | Browser build via WebAssembly |
| `Motely.Web` | The seed lab, a web app on `motely-wasm` and `jaml-ui` |
| `Filters/` | Canonical filter library (used by `--jaml <name>` and `Motely.Web`; `Motely.Tests/JamlFilters/` holds frozen test fixtures) |
| `docs/` | Release notes |

## Filters

A filter lists `must`, `should` and `mustNot` clauses. Each clause is a mapping whose key is the clause type (`joker`, `rareJoker`, `legendaryJoker`, `voucher`, `boss`, `and`, `or`, …), and the loader builds the matching class from that key. The type may also be written as a tag on the clause (`!joker`). A clause with neither does not load.

```yaml
must:
  - rareJoker: Blueprint
    antes: [1, 2, 3, 4]
  - or:
    mode: max
    clauses:
      - joker: Blueprint
      - joker: Brainstorm
should:
  - boss: The Eye
    antes: [3]
```

`and` and `or` hold their children as a list, either directly (`or: [...]`) or under `clauses:`. A filter written as JSON loads the same way.

`soulOrder:` entries may be written as bare legendary names (`soulOrder: [Perkeo, Canio, Perkeo]`); `antes:` on an entry is optional and defaults to any ante. The point of `soulOrder:` is the *order* of legendaries — location is optional and usually already pinned by standalone `legendaryJoker:` clauses.

Some keys and names have a second spelling that loads the same clause:

| Written | Means |
|---|---|
| `ante: 2` | `antes: [2]` (any list of numbers may be one number) |
| `requireMega: true` | `requireMegaPack: true` (under `sources:`) |
| `soulJoker: Perkeo` | `legendaryJoker: Perkeo` |
| `voucher: Reroll Surplus`, `boss: The Hook`, `tarotCard: Fool` | `RerollSurplus`, `TheHook`, `TheFool`: any case, spaces optional, "The" optional, when exactly one name fits |
| `rank: K` | `rank: King` (J, Q, K, A) |
| `dateCreated: 2025-12-25` | metadata, kept and ignored by the search |

**What a clause looks at without `sources:`.** A joker, tarot, planet or spectral clause reads the first eight shop cards of each ante (`shopItems: [0..7]`, the opening shop and about three rerolls); spectral also reads every pack, the others don't. A `joker` clause that names a legendary, or names no joker at all, also counts legendaries as a `legendaryJoker` clause does. A `standardCard` clause reads packs only until it names shop slots. The analyzer (`MotelyJamlyzer`) lists 15 shop cards for antes 0–1 and 50 after, so a card it shows at position 9 or later scores 0 unless the clause names the slot: the 11th card is `sources: { shopItems: [10] }`.

**What the player holds: `with:`.** A clause can say what the player has while it is read. The block is a mapping; each clause type reads the keys that change it and ignores the others.

```yaml
must:
  - voucher: TarotMerchant
    antes: [1]
  - tarotCard: Death
    antes: [2, 3]
    with:
      vouchers: [TarotMerchant]
```

| Key | Read by | What it changes |
|---|---|---|
| `vouchers: [...]` | `joker`, `commonJoker`, `uncommonJoker`, `rareJoker`, `tarotCard`, `planetCard`, `spectralCard`, `standardCard` | The shop the clause reads is rolled with these vouchers redeemed on top of the deck's own. Tarot Merchant/Tycoon and Planet Merchant/Tycoon change how often tarots and planets appear, which moves every other card's slot too. Magic Trick adds playing cards. No other voucher changes the roll: Hone and Glow Up, for one, do nothing here |
| `jokers: [...]` | `legendaryJoker`, `soulOrder` | A held legendary is out of the Soul's pool; holding `Showman` keeps repeats in |
| `luck: X2` | `luckyMoney`, `luckyMult`, `wheelOfFortune`, `wheelStaysFlipped`, `glassDestroy`, `cavendishExtinct`, `grosMichelExtinct`, `spaceLevelup` | The Oops! All 6s multiplier on the odds (X1, X2, X4, X5, X8, X16, X32, X64, or the number) |

Without `with:` a shop clause reads the shop the deck alone gives, whatever vouchers the seed offers. `with:` on any other clause is an unknown property, and `with: ~` does not load. The analyzer takes the same block as an argument (`MotelyJamlyzer.Analyze(config, with: ...)`) and then shows the shop, Souls and luck events a clause with that `with:` reads.

A wrong key or name fails to load with one line, on its own line of the file. An unknown property is named (`Property 'luckMoney' not found`). A name of the wrong kind says what it is not (`'Showman' is not a common joker`). `shopItems:` written beside `joker:` is an unknown property; it belongs under `sources:`.

Limits, each a load error: `*name` aliases aren't read, so write the value out where it's used. A key written twice in one mapping is refused. Every number in a list (`antes`, `rolls`, `shopItems`, `boosterPacks`, other source slots, event rolls) runs from 0 to 999. Antes run from 0 to 39. `min` and `max` are 0 or more, and `max` is not below `min`. An `and` takes no `min` or `max`, and an `or` takes no `max` and needs `min: 1` or more. An `and` or `or` needs at least one clause, and a filter takes at most 256 `should:` clauses. Event, voucher and tag `rolls` need at least one roll, and boss `antes` run from 1.

## CLI

```sh
dotnet run --project Motely.CLI -- --jaml <filter> [mode] [options]
```

`<filter>` is a path, or a bare name that is looked up as `Filters/<name>.yaml` under the current directory.

stdout carries only results (CSV: `seed,score,<tallies>`). Progress, warnings and the summary go to stderr, so `> out.csv` saves a clean file.
Exit codes: `0` finished, `1` bad input or a `--save` that could not write, `130` interrupted (the resume point is printed).

**Choosing seeds** (pick one mode; sequential is the default):

| Flag | Searches |
|---|---|
| *(none)* | Sequential sweep of every seed. Resume with `--startBatch`/`--startSeed`/`--startPercent`, end with `--endBatch`/`--stopSeed`. `--batchCharCount` sets how many trailing characters one batch sweeps (default 3) |
| `--seeds A1B2C3D4,E5F6G7H8` | Exactly these seeds |
| `--seeds file.csv` | Every seed in a file: one per line, first column. Plain lists, motely CSVs, spreadsheet exports, `.parquet`/`.pq`, `.duckdb`/`.db`/`.sqlite`, and remote `https://` or `s3://` Parquet URLs (streamed via DuckDB) all work. Up to 65,536 text seeds print in file order; longer lists split across threads |
| `--seeds -` | Seeds read from stdin |
| `--replay` | The filter file's own `seeds:` block |
| `--keyword WORD` / `--keywords A,B` | Seeds that contain the word(s). `--padding` names the characters that fill the other slots (default: all of 1-9 and A-Z) |
| `--random N` | N random seeds |
| `--aesthetic NAME\|all` | Pattern families such as palindromes |

**Other options:** `--deck`, `--stake` (override the filter's values), `--threads`, `--collect N` (stop after N matches), `--cutoff N|auto|max|off`, `--save` (merge the top seeds into the filter's `seeds:` block), `--save-limit N` (cap saved seeds; default 2000, 0 = unlimited), `--out <file.duckdb|file.parquet>` (stream matches into DuckDB or ZSTD Parquet), `--jamlyze` (with `--out`, store the scoped Jamlyzer JSON window per match), `-q`.

**Cutoff.** `--cutoff N` prints seeds that score at least N. `auto`, the default, prints every match until finds flood in, then only seeds that tie or beat the best score so far. `max` prints only a seed that beats every score before it; with more than one thread the order seeds finish in changes from run to run, so its rows can too (`--threads 1` repeats). `off` prints every match. A filter's own top-level `cutoff: N` is the floor when the flag is not given. Only `should` clauses score, so a filter without them scores every seed 0: a cutoff above zero prints nothing, and `max` prints the first match and stops.

### Researching a pile of seeds

A seed list can be a plain CSV, a `.parquet` file, or a `.duckdb` file, and replaying one takes milliseconds, so keep your finds and re-run filters over them:

```sh
motely --jaml longColaDanker --cutoff off > pile.csv                       # collect CSV (Ctrl+C whenever)
motely --jaml longColaDanker --cutoff off --out finds.parquet --jamlyze    # or stream directly to ZSTD Parquet + Jamlyzer JSON
motely --jaml otherFilter --seeds finds.parquet --cutoff off               # re-score a CSV, Parquet, or DuckDB pile
motely --jaml a --seeds pile.csv -q | motely --jaml b --seeds - -q         # chain filters
motely --jaml longColaDanker --seeds pile.csv --save                       # keep the winners in the filter
```

## For bots: MCP

`Motely.Mcp` speaks MCP over stdio. Point a client at `dotnet run --project Motely.Mcp` (or a published `Motely.Mcp` binary). stdout is reserved for the protocol; Motely's own search stays quiet.

| Tool | What it does |
|---|---|
| `seed_validate` | Load JAML text, a filter path, or a bare `Filters/` name and build the plan. |
| `seed_search` | Search with `replay`, `seeds`, `keyword`, `random` or a bounded `sequential` window. Same mode names and result text as the WebMCP tool in `Motely.Web`. |
| `soul_order` | Report a seed's **anteless** run-global legendary face sequence, plus a direct `Perkeo → Perkeo` answer. `maxAnte` defaults to 39 and `heldJokers` maps to `with: { jokers: [...] }`. |
| `best_seed` | Replay a seed file/CSV/list against a filter, ignore its cutoff, and return the highest-scoring seed and a top-N table. |
| `analyze_seed` | Blueprint-style seed overview. Pass `filter` to use the exact JAML's antes, shop slots and `with:`; the first written block with content is the window. Otherwise use `maxAnte` and `shopLimit`. |

Every tool returns short text plus structured JSON and sets `isError` with an actionable `Error: …` message on bad input. All five are read-only/idempotent. File paths stay inside the current directory or the server's project folder; an absolute path outside those areas returns an `Error: …` instead of opening the file.

Replaying a pile for the best seed is one call:

```json
{ "filter": "longCola", "seeds": "my46kSeeds.csv", "top": 10 }
```

The CLI equivalent is `motely --jaml longCola --seeds my46kSeeds.csv --cutoff off --threads 1`, then sort by score; `--out finds.parquet --jamlyze` keeps the Jamlyzer window beside each find.

## Build and test

```sh
dotnet test Motely.Tests       # builds the engine, the CLI and the suite, and runs it
dotnet build Motely.slnx       # every project, Motely.Wasm included
```

```sh
dotnet run --project Motely.CLI -- schema   # rewrites Motely.Wasm/jaml from the filter loader
```

`Motely.Wasm/jaml/index.d.ts`, `motely-filter.schema.json` and `vocab.json` describe what the loader accepts, for TypeScript and editors. They are generated, never edited: after a change to a clause class, to the rule table in `Motely/Filters/JamlConfigLoader.cs` or to a description in `Motely/Filters/Schema/JamlSchemaAnnotations.cs`, run the command from the repository root and commit the three files. The suite fails while a committed file differs from what the command writes. `--out <folder>` writes somewhere else.

`Motely.slnx` holds `Motely.Wasm`, which needs `dotnet workload install wasm-tools` and, for its folder access, the Bootsharp.FileSystem sponsor feed. Without the feed, add `-p:MotelyFileSystem=false`. `Motely.Wasm/README.md` has the rest.
