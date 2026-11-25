# Motely architecture

Motely is a Balatro seed search engine with a declarative filter language (JAML).
This document is for contributors. For usage, see `README.md`.

## Layers

1. **JAML language + loader** (`Motely/Filters/JamlConfig*.cs`, `JamlClauseBinding.cs`,
   `ClauseKeyDeserializer.cs`, `JamlParser.cs`, `MotelyClause.cs`, `LogicClause.cs`).
   - `JamlConfig` is the document shape (deck, stake, seeds, must/should/mustNot).
   - `JamlConfigLoader.FromJaml` deserializes via YamlDotNet's static (AOT) deserializer,
     with custom node deserializers for clause discrimination and scalar-or-list conversion.
   - `JamlClauseBinding.ClauseTypeByKey` maps a document key (`joker`, `or`, …) to a clause
     type. A clause with neither a known key nor a `!tag` does not load.
   - `JamlConfigLoader.ClauseRules` is the single rule table: it drives both runtime
     enforcement and JSON Schema emission (`JamlSchemaGenerator`). Generated artifacts
     (`Motely.Wasm/jaml/*`) are checked in and tested for parity.

2. **Clause model** (`MotelyClause` subclasses in `Filters/AnteCards`, `AnteFeatures`,
   `Events`). Capability interfaces: `IAnteScopedClause` (antes), `IRollScopedClause`
   (rolls), `IWithScopedClause` (what the player holds). `LogicClause` is `and`/`or`.

3. **Filter descs** (`IMotelySeedFilterDesc`). Each clause becomes a SIMD filter
   `Filter(ref MotelyVectorSearchContext) -> VectorMask`. `MotelySearchBuilder.CreateSettings`
   turns a `JamlConfig` into an `IMotelySearchSettings`: cheapest must clauses first,
   exact mustNot clauses negated, scoring pass attached.

4. **Search engine** (`MotelySearch<TBaseFilter>`). SIMD filter chain per batch of 8 lanes,
   then scalar scoring pass (`JamlShouldScoreDesc` / `ClauseScoring`). Sequential or
   provider seed modes, multi-threaded, with auto score cutoff.

5. **Jamlyzer** (`Motely/Analysis`). Per-seed breakdown + resumable stream states.
   Attaches as an analyze provider on a search.

## The parity contract (the important part)

Every clause has **two** implementations that must agree:

- a SIMD `Filter` (in `*FilterDesc.cs`) producing a `VectorMask` over 8 lanes;
- a scalar `Count*Occurrences` (in `ClauseScoring.cs`) used by scoring and by
  `SearchIndividualSeeds` confirmation.

They must agree on occurrence counting, `with:` handling, and PRNG stream advancement.
The engine relies on this for correctness of `must`/`mustNot` and the auto cutoff.

`ClauseScoring.IsExactFilterConfirm` classifies, per clause type, whether the SIMD
prefilter is "the same law as scoring". Misclassification changes which seeds are
reported. The classification is currently hand-maintained; treat it as load-bearing.

Tests: `VectorScalarParityTests`, `JokerDescScalarAgreementTests`, `JamlMatchCoreParityTests`.

## Adding a clause (checklist)

1. Add the clause class (capability interfaces + `CreateFilterDesc`).
2. Register its key in `JamlClauseBinding.ClauseTypeByKey` (+ tag mapping is automatic).
3. Add alias converters if needed in `JamlClauseBinding.Converters`.
4. Implement the filter desc (`*FilterDesc.cs`) including SIMD `Filter`.
5. Add scalar counting in `ClauseScoring.CountOccurrences` switch + `CountRawOccurrences`.
6. Add tally label arm in `MotelySearchBuilder.ClauseName`.
7. Add cost estimate in `MotelySearchBuilder.EstimateSimdCost`.
8. Add exact-confirm classification in `ClauseScoring.IsExactFilterConfirm`.
9. Add schema annotations in `Motely/Filters/Schema/JamlSchemaAnnotations.cs`.
10. Run `dotnet run --project Motely.CLI -- schema` and commit generated files.
11. Add parity + load tests.

## Concurrency notes

`MotelySearch` supports inline runs (materialized seed lists, browser) and threaded runs.
Disposal is deferred when a run is in flight. Worker threads are joined before native
buffers are freed. `_inlineRunGate` / `_lifecycleGate` serialize these paths. Treat
changes here with care.