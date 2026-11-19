import { lazy, Suspense, useCallback, useEffect, useState } from "react";
import {
  JimboApp,
  JimboBackground,
  JimboBalatroFooter,
  JimboBadge,
  JimboPanel,
  JimboText,
  JimboWordmark,
  SeedLab,
  STARTER_JAML,
} from "jaml-ui";
import { JimboTabs } from "jaml-ui/ui";
import { FilterFolder, type OpenedFilter } from "./FilterFolder.js";
import { buildSeedLabYaml } from "./filterLibrary.js";
import { SeedPile, type IncomingPile } from "./SeedPile.js";
import { useSeedLabWebMCP } from "./webmcp.js";

const FilterLibrary = lazy(() =>
  import("./FilterLibrary.js").then((m) => ({ default: m.FilterLibrary })),
);
const AgentPanel = lazy(() =>
  import("./AgentPanel.js").then((m) => ({ default: m.AgentPanel })),
);

const SEED_RE = /^[1-9A-Z]{0,8}$/;

/** The pile is the front page; filters holds the built-in corpus; lab is the editor; agent connects to Motely.MCP. */
type TabId = "pile" | "filters" | "lab" | "agent";

function tabFrom(param: string | undefined, fallback: TabId): TabId {
  if (param === "agent" || param === "server" || param === "ripper" || param === "mcp") return "agent";
  if (param === "filters" || param === "library") return "filters";
  if (param === "lab") return "lab";
  if (param === "pile") return "pile";
  return fallback;
}

function normalizeSeed(raw: string | null | undefined): string | null {
  if (raw === null || raw === undefined) return null;
  if (raw.length === 0) return "";
  const trimmed = raw.trim();
  if (trimmed === '""' || trimmed === "''") return "";
  if (trimmed.length === 0) return null;
  const upper = trimmed.toUpperCase().replace(/0/g, "O");
  return SEED_RE.test(upper) ? upper : null;
}

function getQueryParams(): {
  tab: TabId;
  initialOpened: OpenedFilter | null;
  agentInput: string;
} {
  if (typeof window === "undefined") {
    return { tab: "pile", initialOpened: null, agentInput: "" };
  }
  try {
    const params = new URLSearchParams(window.location.search);
    const tabParam = params.get("tab")?.toLowerCase();
    // A link that carries a seed or a filter was made for the lab, so it still opens there.
    const tab = tabFrom(tabParam, params.has("seed") || params.has("filter") ? "lab" : "pile");
    const seed = params.has("seed") ? normalizeSeed(params.get("seed")) : null;
    const seedDisplay = seed === "" ? '""' : seed;
    const filter = params.get("filter")?.trim();
    const prompt = params.get("prompt")?.trim();

    let initialOpened: OpenedFilter | null = null;
    if (seed !== null && filter) {
      const hasSeeds = /^\s*seeds\s*:/m.test(filter);
      const yaml = hasSeeds ? filter : `seeds:\n  - ${JSON.stringify(seed)}\n${filter}`;
      initialOpened = { name: `Seed: ${seedDisplay}`, yaml };
    } else if (seed !== null) {
      initialOpened = {
        name: `Seed: ${seedDisplay}`,
        yaml: `seeds:\n  - ${JSON.stringify(seed)}\n${STARTER_JAML}`,
      };
    } else if (filter) {
      initialOpened = { name: "Custom Filter", yaml: filter };
    }

    const agentInput = prompt || (seed !== null ? `analyze ${seedDisplay}` : "");
    return { tab, initialOpened, agentInput };
  } catch {
    return { tab: "pile", initialOpened: null, agentInput: "" };
  }
}

export function App({ folders }: { folders: boolean }) {
  const [initialParams] = useState(getQueryParams);
  const [opened, setOpened] = useState<OpenedFilter | null>(initialParams.initialOpened);
  const [openedSeq, setOpenedSeq] = useState(0);
  const [tab, setTab] = useState<TabId>(initialParams.tab);
  const [agentInput, setAgentInput] = useState<string>(initialParams.agentInput);
  const [incomingPile, setIncomingPile] = useState<IncomingPile | null>(null);

  // Sync state if URL changes externally (e.g. browser back/forward)
  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => {
      const q = getQueryParams();
      setTab(q.tab);
      setOpened(q.initialOpened);
      setAgentInput(q.agentInput);
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const syncUrlTab = useCallback((nextTab: TabId) => {
    if (typeof window !== "undefined") {
      const url = new URL(window.location.href);
      url.searchParams.set("tab", nextTab);
      window.history.replaceState({}, "", url);
    }
  }, []);

  const handleTabChange = useCallback(
    (id: string) => {
      const nextTab = tabFrom(id, "pile");
      setTab(nextTab);
      syncUrlTab(nextTab);
    },
    [syncUrlTab],
  );

  const handleOpenFilter = useCallback(
    (filter: OpenedFilter) => {
      setOpened(filter);
      setOpenedSeq((n) => n + 1);
      setTab("lab");
      syncUrlTab("lab");
    },
    [syncUrlTab],
  );

  const handleInspectSeeds = useCallback(
    (pile: { seeds: string[]; deck: string; stake: string; label: string }) => {
      setIncomingPile({
        key: Date.now(),
        seeds: pile.seeds,
        deck: pile.deck,
        stake: pile.stake,
      });
      setTab("pile");
      syncUrlTab("pile");
    },
    [syncUrlTab],
  );

  const handleOpenAgentSeeds = useCallback(
    (seeds: string[]) => {
      if (seeds.length === 0) return;
      setIncomingPile({
        key: Date.now(),
        seeds,
      });
      setTab("pile");
      syncUrlTab("pile");
    },
    [syncUrlTab],
  );

  const handleInspectInLab = useCallback(
    (rawSeed: string, deck = "Red", stake = "White") => {
      const seed = normalizeSeed(rawSeed);
      if (seed === null) return;
      const seedDisplay = seed === "" ? '""' : seed;
      setOpened({
        name: `Seed: ${seedDisplay}`,
        yaml: buildSeedLabYaml(seed, deck, stake, STARTER_JAML),
      });
      setOpenedSeq((n) => n + 1);
      setTab("lab");
      if (typeof window !== "undefined") {
        const url = new URL(window.location.href);
        url.searchParams.set("tab", "lab");
        url.searchParams.set("seed", seed);
        window.history.pushState({}, "", url);
      }
    },
    [],
  );

  // WebMCP tools are registered for as long as the lab is mounted; the badge says an
  // in-page agent (native WebMCP) can drive seed_validate / seed_search directly.
  const agentReady = useSeedLabWebMCP();

  const tabs = [
    { id: "pile", label: "Seed Pile" },
    { id: "filters", label: "Filters" },
    { id: "lab", label: "Seed Lab" },
    { id: "agent", label: "SIMD Ripper" },
  ];

  return (
    <div style={{ minHeight: "100dvh", display: "flex", flexDirection: "column", paddingBottom: 72 }}>
      <JimboBackground />
      <JimboApp
        variant="page"
        style={{ flex: 1, minHeight: "auto", gridTemplateRows: "auto auto 1fr", alignContent: "start" }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.6rem", flexWrap: "wrap" }}>
          <JimboWordmark title="Motely" sub="Balatro seed search — WASM in-browser & Motely.MCP SIMD Ripper" />
          {agentReady ? (
            <JimboBadge
              size="sm"
              tone="grey"
              title="WebMCP ready: this page exposes seed_validate and seed_search to in-page agents"
            >
              agent-ready
            </JimboBadge>
          ) : null}
        </div>
        <JimboTabs tabs={tabs} activeTab={tab} onTabChange={handleTabChange} />
        {tab === "pile" ? (
          <SeedPile incomingPile={incomingPile} onInspectInLab={handleInspectInLab} />
        ) : tab === "filters" ? (
          <Suspense
            fallback={
              <JimboPanel>
                <JimboText size="sm" tone="grey">
                  Loading Filter Library...
                </JimboText>
              </JimboPanel>
            }
          >
            <FilterLibrary onOpenInLab={handleOpenFilter} onInspectSeeds={handleInspectSeeds} />
          </Suspense>
        ) : tab === "agent" ? (
          <Suspense
            fallback={
              <JimboPanel>
                <JimboText size="sm" tone="grey">
                  Loading SIMD Ripper...
                </JimboText>
              </JimboPanel>
            }
          >
            <AgentPanel
              onInspect={handleInspectInLab}
              onOpenPile={handleOpenAgentSeeds}
              initialInput={agentInput}
            />
          </Suspense>
        ) : (
          <>
            {folders ? <FilterFolder opened={opened} onOpen={setOpened} /> : null}
            {/* Opening a filter remounts the lab with that text: a new filter is a new search. */}
            <SeedLab
              key={`${opened?.name ?? "starter"}:${openedSeq}`}
              defaultJaml={opened?.yaml ?? STARTER_JAML}
            />
          </>
        )}
      </JimboApp>
      {/* Pinned to the viewport bottom, like Balatro's page chrome — content scrolls under
          the 72px of padding above, so nothing ever hides behind it. */}
      <JimboBalatroFooter style={{ position: "fixed", bottom: 0, left: 0, right: 0, marginTop: 0 }} />
    </div>
  );
}
