import { useMemo, useState } from "react";
import { JimboBadge, JimboButton, JimboPanel, JimboText } from "jaml-ui";
import { JimboTextInput } from "jaml-ui/ui";
import {
  FILTER_CATEGORIES,
  parseFilterMetadata,
  searchFilters,
  type FilterCategory,
  type FilterSummary,
} from "./filterLibrary.js";
import type { OpenedFilter } from "./FilterFolder.js";

// Eagerly bundle the compact Filters/*.yaml corpus (~64 KB raw), while lazy-loading
// the 600 KB longCola.yaml archive on demand so the Filters tab stays tiny and instant.
const compactFilterModules = import.meta.glob<string>(
  ["../../Filters/*.yaml", "!../../Filters/longCola.yaml"],
  { query: "?raw", import: "default", eager: true },
);

const largeFilterLoaders = import.meta.glob<string>("../../Filters/longCola.yaml", {
  query: "?raw",
  import: "default",
});

const LARGE_FILTER_META: FilterSummary = {
  id: "longCola",
  fileName: "longCola.yaml",
  name: "simpleCola (Archive)",
  author: "pifreak",
  description:
    "Diet Cola Ante 11 with Negative Tag, Perkeo, Ankh, Blueprint/Brainstorm/Invisible Joker, plus a massive saved seed archive (loaded on click).",
  deck: "Ghost",
  stake: "White",
  category: "featured",
  mustCount: 6,
  shouldCount: 2,
  mustNotCount: 0,
  seeds: [],
  seedCount: 0,
  lazy: true,
};

const ALL_FILTERS: readonly FilterSummary[] = (() => {
  const entries: FilterSummary[] = Object.entries(compactFilterModules).map(([path, yaml]) =>
    parseFilterMetadata(path, yaml),
  );
  if (Object.keys(largeFilterLoaders).length > 0) {
    entries.push(LARGE_FILTER_META);
  }
  return entries;
})();

const PAGE_SIZE = 24;

export interface FilterLibraryProps {
  onOpenInLab: (filter: OpenedFilter) => void;
  onInspectSeeds: (pile: { seeds: string[]; deck: string; stake: string; label: string }) => void;
}

export function FilterLibrary({ onOpenInLab, onInspectSeeds }: FilterLibraryProps) {
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState<FilterCategory>("all");
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);
  const [loadingId, setLoadingId] = useState<string | null>(null);

  const counts = useMemo(() => {
    const map: Record<string, number> = { all: ALL_FILTERS.length };
    for (const f of ALL_FILTERS) {
      map[f.category] = (map[f.category] ?? 0) + 1;
    }
    return map;
  }, []);

  const filtered = useMemo(
    () => searchFilters(ALL_FILTERS, query, category),
    [query, category],
  );

  const visible = filtered.slice(0, visibleCount);

  async function resolveFilter(entry: FilterSummary): Promise<FilterSummary> {
    if (entry.yaml) return entry;
    const loader = Object.entries(largeFilterLoaders).find(([path]) =>
      path.endsWith(`/${entry.fileName}`),
    )?.[1];
    if (!loader) return entry;
    const yaml = await loader();
    return parseFilterMetadata(entry.fileName, yaml);
  }

  async function handleOpen(entry: FilterSummary) {
    setLoadingId(entry.id);
    try {
      const full = await resolveFilter(entry);
      if (full.yaml) {
        onOpenInLab({ name: full.fileName, yaml: full.yaml });
      }
    } finally {
      setLoadingId(null);
    }
  }

  async function handleInspect(entry: FilterSummary) {
    setLoadingId(entry.id);
    try {
      const full = await resolveFilter(entry);
      if (full.seeds.length > 0) {
        onInspectSeeds({
          seeds: full.seeds,
          deck: full.deck,
          stake: full.stake,
          label: full.name,
        });
      }
    } finally {
      setLoadingId(null);
    }
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <JimboPanel title={`Filter Library (${ALL_FILTERS.length})`}>
        <div style={{ display: "flex", flexDirection: "column", gap: "0.65rem" }}>
          <JimboText size="sm" tone="grey">
            Browse all {ALL_FILTERS.length} JAML filters from the Motely repository. Open any filter
            in Seed Lab to run a browser sweep, or inspect its saved seeds right in Seed Pile.
          </JimboText>

          <JimboTextInput
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setVisibleCount(PAGE_SIZE);
            }}
            placeholder="Search filters by joker, voucher, tag, boss, deck, or author (e.g. Perkeo, Blueprint, Anaglyph)..."
            aria-label="Search filters"
          />

          <div style={{ display: "flex", flexWrap: "wrap", gap: "0.35rem" }}>
            {FILTER_CATEGORIES.map((cat) => {
              const count = counts[cat.id] ?? 0;
              if (cat.id !== "all" && count === 0) return null;
              const active = category === cat.id;
              return (
                <JimboButton
                  key={cat.id}
                  size="xs"
                  tone={active ? "orange" : "grey"}
                  onClick={() => {
                    setCategory(cat.id);
                    setVisibleCount(PAGE_SIZE);
                  }}
                >
                  {cat.label} ({count})
                </JimboButton>
              );
            })}
          </div>
        </div>
      </JimboPanel>

      {filtered.length === 0 ? (
        <JimboPanel>
          <JimboText size="sm" tone="grey">
            No filters matched &ldquo;{query}&rdquo;. Try searching for a joker like Perkeo,
            Blueprint, Baron, or a deck like Ghost.
          </JimboText>
        </JimboPanel>
      ) : (
        <>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(290px, 1fr))",
              gap: "0.65rem",
            }}
          >
            {visible.map((entry) => {
              const busy = loadingId === entry.id;
              return (
                <div
                  key={entry.id}
                  className="j-panel"
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    gap: "0.55rem",
                    padding: "0.75rem",
                  }}
                >
                  <div style={{ display: "flex", flexDirection: "column", gap: "0.4rem" }}>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: "0.4rem",
                        flexWrap: "wrap",
                      }}
                    >
                      <JimboText size="md" tone="gold">
                        {entry.name}
                      </JimboText>
                      <JimboBadge
                        size="sm"
                        tone={entry.category === "featured" ? "orange" : "blue"}
                      >
                        {entry.category}
                      </JimboBadge>
                    </div>

                    <div style={{ display: "flex", flexWrap: "wrap", gap: "0.3rem" }}>
                      <JimboBadge size="sm" tone="grey">
                        {entry.deck} · {entry.stake}
                      </JimboBadge>
                      {entry.mustCount > 0 && (
                        <JimboBadge size="sm" tone="green">
                          {entry.mustCount} must
                        </JimboBadge>
                      )}
                      {entry.shouldCount > 0 && (
                        <JimboBadge size="sm" tone="blue">
                          {entry.shouldCount} should
                        </JimboBadge>
                      )}
                      {entry.mustNotCount > 0 && (
                        <JimboBadge size="sm" tone="red">
                          {entry.mustNotCount} mustNot
                        </JimboBadge>
                      )}
                      {entry.seedCount > 0 && (
                        <JimboBadge size="sm" tone="orange">
                          {entry.seedCount.toLocaleString()} seeds
                        </JimboBadge>
                      )}
                    </div>

                    <JimboText size="xs" tone="grey">
                      {entry.description ||
                        `${entry.fileName} — ${entry.mustCount} required clause${entry.mustCount === 1 ? "" : "s"}.`}
                    </JimboText>
                  </div>

                  <div style={{ display: "flex", gap: "0.4rem", flexWrap: "wrap" }}>
                    <JimboButton
                      size="sm"
                      tone="green"
                      disabled={busy}
                      onClick={() => void handleOpen(entry)}
                    >
                      {busy ? "Loading..." : "Open in Lab"}
                    </JimboButton>
                    {(entry.seedCount > 0 || entry.lazy) && (
                      <JimboButton
                        size="sm"
                        tone="blue"
                        disabled={busy}
                        onClick={() => void handleInspect(entry)}
                      >
                        {entry.lazy
                          ? "Load Saved Seeds"
                          : `Inspect ${entry.seedCount} Seed${entry.seedCount === 1 ? "" : "s"}`}
                      </JimboButton>
                    )}
                  </div>
                </div>
              );
            })}
          </div>

          {filtered.length > visibleCount && (
            <div style={{ display: "flex", justifyContent: "center" }}>
              <JimboButton
                tone="blue"
                size="md"
                onClick={() => setVisibleCount((c) => c + PAGE_SIZE)}
              >
                Show More ({(filtered.length - visibleCount).toLocaleString()} remaining)
              </JimboButton>
            </div>
          )}
        </>
      )}
    </div>
  );
}
