// Pure filter library parsing, categorization, search, and starter seed piles.
// Kept React-free and Vite-free so node:test verifies it directly against Filters/*.yaml.

export type FilterCategory =
  | "all"
  | "featured"
  | "legendary"
  | "rare"
  | "uncommon"
  | "common"
  | "spectral"
  | "tarot"
  | "tag"
  | "boss"
  | "deck"
  | "standard";

export type ConcreteFilterCategory = Exclude<FilterCategory, "all">;

export const FILTER_CATEGORIES: readonly { id: FilterCategory; label: string }[] = [
  { id: "all", label: "All" },
  { id: "featured", label: "Featured" },
  { id: "legendary", label: "Legendary" },
  { id: "rare", label: "Rare" },
  { id: "uncommon", label: "Uncommon" },
  { id: "common", label: "Common" },
  { id: "spectral", label: "Spectral" },
  { id: "tarot", label: "Tarot" },
  { id: "tag", label: "Tags" },
  { id: "boss", label: "Bosses" },
  { id: "deck", label: "Decks" },
  { id: "standard", label: "Standard" },
];

export interface FilterSummary {
  id: string;
  fileName: string;
  name: string;
  author: string | null;
  description: string | null;
  deck: string;
  stake: string;
  category: ConcreteFilterCategory;
  mustCount: number;
  shouldCount: number;
  mustNotCount: number;
  seeds: string[];
  seedCount: number;
  yaml?: string;
  lazy?: boolean;
}

export interface StarterPile {
  id: string;
  label: string;
  subtitle: string;
  deck: string;
  stake: string;
  watch: string;
  seeds: readonly string[];
}

export const STARTER_PILES: readonly StarterPile[] = [
  {
    id: "classic",
    label: "Classic Seeds",
    subtitle: "ALEEB, PIROCKS, WEEJOKER, DWDH25ZE",
    deck: "Red",
    stake: "White",
    watch: "Oops All 6s, Ankh, Blueprint, Brainstorm, Coupon Tag",
    seeds: ["ALEEB", "PIROCKS", "WEEJOKER", "DWDH25ZE"],
  },
  {
    id: "zerkeo",
    label: "Zerkeo (Neg Perkeo)",
    subtitle: "Anaglyph · Ante 0 Negative Perkeo + Hieroglyph",
    deck: "Anaglyph",
    stake: "White",
    watch: "Perkeo, Hieroglyph, Blueprint, Brainstorm",
    seeds: ["F2U88X11", "JX8C8X11", "L8FJ8X11", "A68EBX11"],
  },
  {
    id: "blueprint",
    label: "Blueprint & Economy",
    subtitle: "84111111, ALEEB, 12345678",
    deck: "Red",
    stake: "White",
    watch: "Blueprint, Brainstorm, Telescope, Magic Trick",
    seeds: ["84111111", "ALEEB", "12345678"],
  },
];

const SEED_RE = /^[1-9A-Z]{1,8}$/;

function normalizeSeedToken(raw: string): string | null {
  const cleaned = raw.trim().replace(/^["']|["']$/g, "").trim().toUpperCase().replace(/0/g, "O");
  return SEED_RE.test(cleaned) ? cleaned : null;
}

function unquoteScalar(value: string): string {
  const trimmed = value.trim();
  if (
    trimmed.length >= 2 &&
    ((trimmed.startsWith('"') && trimmed.endsWith('"')) ||
      (trimmed.startsWith("'") && trimmed.endsWith("'")))
  ) {
    return trimmed.slice(1, -1);
  }
  return trimmed;
}

export function categorizeFilter(id: string): ConcreteFilterCategory {
  const lower = id.toLowerCase();
  if (lower.startsWith("legendary-")) return "legendary";
  if (lower.startsWith("rare-")) return "rare";
  if (lower.startsWith("uncommon-")) return "uncommon";
  if (lower.startsWith("common-")) return "common";
  if (lower.startsWith("spectral-")) return "spectral";
  if (lower.startsWith("tarot-")) return "tarot";
  if (lower.startsWith("tag-")) return "tag";
  if (lower.startsWith("boss-")) return "boss";
  if (lower.startsWith("deck-")) return "deck";
  if (lower.startsWith("standard-")) return "standard";
  return "featured";
}

function humanizeId(id: string): string {
  return id
    .replace(/[-_]+/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/\b\w/g, (ch) => ch.toUpperCase());
}

/**
 * Extracts display metadata, clause counts, and saved seeds from a JAML filter file.
 * Supports single-line scalars as well as YAML folded (`>`) and literal (`|`) block scalars.
 */
export function parseFilterMetadata(fileName: string, yaml: string): FilterSummary {
  const baseName = fileName.split("/").pop() ?? fileName;
  const id = baseName.replace(/\.(ya?ml|jaml)$/i, "");
  const lines = yaml.split(/\r?\n/);

  let name: string | null = null;
  let author: string | null = null;
  let description: string | null = null;
  let deck = "Red";
  let stake = "White";

  let mustCount = 0;
  let shouldCount = 0;
  let mustNotCount = 0;
  const seeds: string[] = [];
  const seenSeeds = new Set<string>();

  const addSeed = (token: string) => {
    const normalized = normalizeSeedToken(token);
    if (normalized && !seenSeeds.has(normalized)) {
      seenSeeds.add(normalized);
      seeds.push(normalized);
    }
  };

  type Section = "must" | "should" | "mustNot" | "seeds" | null;
  let section: Section = null;
  let sectionListIndent: number | null = null;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;

    const indent = line.length - line.trimStart().length;

    // Top-level key
    if (indent === 0) {
      section = null;
      sectionListIndent = null;

      const colonIdx = line.indexOf(":");
      if (colonIdx === -1) continue;
      const key = line.slice(0, colonIdx).trim();
      const rest = line.slice(colonIdx + 1).trim();

      if (key === "name" && rest) {
        name = unquoteScalar(rest);
      } else if (key === "author" && rest) {
        author = unquoteScalar(rest);
      } else if (key === "deck" && rest) {
        deck = unquoteScalar(rest);
      } else if (key === "stake" && rest) {
        stake = unquoteScalar(rest);
      } else if (key === "description") {
        if (/^[>|][+-]?$/.test(rest)) {
          const isFolded = rest.startsWith(">");
          const blockLines: string[] = [];
          while (i + 1 < lines.length) {
            const next = lines[i + 1];
            const nextTrimmed = next.trim();
            if (!nextTrimmed) {
              blockLines.push("");
              i++;
              continue;
            }
            const nextIndent = next.length - next.trimStart().length;
            if (nextIndent === 0) break;
            blockLines.push(nextTrimmed);
            i++;
          }
          description = (isFolded ? blockLines.join(" ") : blockLines.join("\n"))
            .replace(/\s+/g, " ")
            .trim();
        } else if (rest) {
          description = unquoteScalar(rest);
        }
      } else if (key === "must" || key === "should" || key === "mustNot") {
        section = key;
        if (rest.startsWith("[") && rest.endsWith("]")) {
          const inner = rest.slice(1, -1).trim();
          if (inner) {
            const count = inner.split(",").filter((s) => s.trim().length > 0).length;
            if (key === "must") mustCount += count;
            else if (key === "should") shouldCount += count;
            else mustNotCount += count;
          }
          section = null;
        }
      } else if (key === "seeds") {
        if (rest.startsWith("[") && rest.endsWith("]")) {
          rest
            .slice(1, -1)
            .split(",")
            .forEach(addSeed);
        } else {
          section = "seeds";
        }
      }
      continue;
    }

    // Inside top-level section
    if (section === "seeds") {
      if (trimmed.startsWith("- ")) {
        addSeed(trimmed.slice(2));
      }
    } else if (section === "must" || section === "should" || section === "mustNot") {
      if (trimmed.startsWith("- ") || trimmed === "-") {
        if (sectionListIndent === null) sectionListIndent = indent;
        if (indent === sectionListIndent) {
          if (section === "must") mustCount++;
          else if (section === "should") shouldCount++;
          else mustNotCount++;
        }
      }
    }
  }

  return {
    id,
    fileName: baseName,
    name: name || humanizeId(id),
    author,
    description,
    deck,
    stake,
    category: categorizeFilter(id),
    mustCount,
    shouldCount,
    mustNotCount,
    seeds,
    seedCount: seeds.length,
    yaml,
  };
}

export function searchFilters(
  filters: readonly FilterSummary[],
  query: string,
  category: FilterCategory,
): FilterSummary[] {
  const tokens = query
    .trim()
    .toLowerCase()
    .split(/\s+/)
    .filter((t) => t.length > 0);

  const matched = filters.filter((f) => {
    if (category !== "all" && f.category !== category) return false;
    if (tokens.length === 0) return true;
    const haystack = [
      f.name,
      f.id,
      f.description ?? "",
      f.author ?? "",
      f.deck,
      f.stake,
      f.category,
      f.yaml ?? "",
    ]
      .join(" ")
      .toLowerCase();
    return tokens.every((tok) => haystack.includes(tok));
  });

  return matched.sort((a, b) => {
    if (category === "all" && tokens.length === 0) {
      if (a.category === "featured" && b.category !== "featured") return -1;
      if (a.category !== "featured" && b.category === "featured") return 1;
    }
    return a.name.localeCompare(b.name);
  });
}

/** Builds a clean JAML document targeting one seed, deck, and stake for SeedLab. */
export function buildSeedLabYaml(
  seed: string,
  deck: string,
  stake: string,
  starterJaml: string,
): string {
  const withoutHeader = starterJaml
    .replace(/^\s*deck\s*:.*\r?\n?/m, "")
    .replace(/^\s*stake\s*:.*\r?\n?/m, "")
    .replace(/^\s*seeds\s*:[\s\S]*?(?=^\S|\Z)/m, "")
    .trimStart();
  return `deck: ${deck}\nstake: ${stake}\nseeds:\n  - ${JSON.stringify(seed)}\n${withoutHeader}`;
}
