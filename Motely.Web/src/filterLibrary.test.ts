import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import bootsharp, { JamlConfigLoader } from "motely-wasm";
import {
  buildSeedLabYaml,
  categorizeFilter,
  parseFilterMetadata,
  searchFilters,
  STARTER_PILES,
} from "./filterLibrary.ts";

const FILTERS_DIR = path.resolve(import.meta.dirname, "../../Filters");

test("categorizeFilter classifies prefixes and featured filters accurately", () => {
  assert.equal(categorizeFilter("legendary-perkeo"), "legendary");
  assert.equal(categorizeFilter("rare-blueprint"), "rare");
  assert.equal(categorizeFilter("uncommon-mime"), "uncommon");
  assert.equal(categorizeFilter("common-joker"), "common");
  assert.equal(categorizeFilter("spectral-ankh"), "spectral");
  assert.equal(categorizeFilter("tarot-death"), "tarot");
  assert.equal(categorizeFilter("tag-negative"), "tag");
  assert.equal(categorizeFilter("boss-the-eye"), "boss");
  assert.equal(categorizeFilter("deck-ghost"), "deck");
  assert.equal(categorizeFilter("standard-glass-ace"), "standard");
  assert.equal(categorizeFilter("Zerkeo_Pure"), "featured");
  assert.equal(categorizeFilter("oops-printer"), "featured");
});

test("parseFilterMetadata parses folded descriptions, clause counts, and seeds blocks", () => {
  const zerkeoYaml = fs.readFileSync(path.join(FILTERS_DIR, "Zerkeo_Pure.yaml"), "utf8");
  const zerkeo = parseFilterMetadata("Zerkeo_Pure.yaml", zerkeoYaml);
  assert.equal(zerkeo.name, "Zerkeo");
  assert.equal(zerkeo.deck, "Anaglyph");
  assert.equal(zerkeo.stake, "White");
  assert.equal(zerkeo.category, "featured");
  assert.equal(zerkeo.mustCount, 3);
  assert.equal(zerkeo.shouldCount, 7);
  assert.ok(zerkeo.seedCount >= 4);
  assert.equal(zerkeo.seeds[0], "F2U88X11");

  const retriggerYaml = fs.readFileSync(
    path.join(FILTERS_DIR, "negative-retrigger-party.yaml"),
    "utf8",
  );
  const retrigger = parseFilterMetadata("negative-retrigger-party.yaml", retriggerYaml);
  assert.equal(retrigger.name, "Negative Retrigger Party");
  assert.equal(retrigger.author, "pifreak");
  assert.ok(retrigger.description?.includes("Anaglyph's post-boss Double Tag"));
  assert.equal(retrigger.mustCount, 3);

  const pickleYaml = fs.readFileSync(path.join(FILTERS_DIR, "Pickle.yaml"), "utf8");
  const pickle = parseFilterMetadata("Pickle.yaml", pickleYaml);
  assert.equal(pickle.name, "Pickle");
  assert.equal(pickle.deck, "Red");
  assert.equal(pickle.mustCount, 1);
});

test("every filter in Filters/*.yaml parses cleanly and searchFilters finds expected results", async () => {
  const files = fs
    .readdirSync(FILTERS_DIR)
    .filter((f) => f.endsWith(".yaml"))
    .sort();
  assert.ok(files.length >= 180, `expected >= 180 filters, got ${files.length}`);

  const parsed = files.map((file) =>
    parseFilterMetadata(file, fs.readFileSync(path.join(FILTERS_DIR, file), "utf8")),
  );

  // Featured filters appear first when browsing All with empty query
  const all = searchFilters(parsed, "", "all");
  assert.equal(all.length, files.length);
  assert.equal(all[0].category, "featured");

  // Multi-token search narrows accurately
  const perkeoAnaglyph = searchFilters(parsed, "perkeo anaglyph", "all");
  assert.ok(perkeoAnaglyph.some((f) => f.id === "Zerkeo_Pure"));

  // Category filter strictly restricts results
  const bosses = searchFilters(parsed, "", "boss");
  assert.ok(bosses.length >= 10);
  assert.ok(bosses.every((f) => f.category === "boss"));

  // Verify sample filters load cleanly in the real motely-wasm 32.x engine
  if (bootsharp.getStatus() !== bootsharp.BootStatus.Booted) await bootsharp.boot();
  for (const sample of ["Zerkeo_Pure.yaml", "legendary-perkeo.yaml", "Pickle.yaml", "oops-printer.yaml"]) {
    const yaml = fs.readFileSync(path.join(FILTERS_DIR, sample), "utf8");
    assert.equal(JamlConfigLoader.check(yaml), null, `${sample} should pass JamlConfigLoader.check`);
  }
});

test("buildSeedLabYaml and STARTER_PILES produce valid JAML", async () => {
  if (bootsharp.getStatus() !== bootsharp.BootStatus.Booted) await bootsharp.boot();
  for (const pile of STARTER_PILES) {
    assert.ok(pile.seeds.length > 0);
    const yaml = buildSeedLabYaml(
      pile.seeds[0],
      pile.deck,
      pile.stake,
      "deck: Red\nstake: White\nmust:\n  - joker: Blueprint\n    antes: [1]\n",
    );
    assert.equal(JamlConfigLoader.check(yaml), null, `starter pile ${pile.id} should build valid JAML`);
  }
});
