# Agent notes

Start with README.md. It covers how to run the CLI, seed files and configuration.

- To check a seed or a filter, run it. Replaying a seed list takes milliseconds:
  - `dotnet run --project Motely.CLI -- --jaml <filter> --seeds <file|list|-> --cutoff off`
- Work from the current code and tests in the workspace.
- Seed lists are plain files (motely CSV output: `seed,score,...`).
- Tests: `dotnet test Motely.Tests` or `dotnet test Motely.slnx`. The solution builds cleanly across projects.
- Search-mode parsing for the CLI lives in `Motely/CliSearchMode.cs`.
- Use friendly, clear words in docs, filters, and comments: search party, find, save, community, play, fun.
- `Motely.Wasm/jaml/index.d.ts`, `motely-filter.schema.json` and `vocab.json` are generated from the loader. Never edit them: run `dotnet run --project Motely.CLI -- schema` and commit what it writes.
