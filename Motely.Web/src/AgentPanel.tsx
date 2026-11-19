import { useEffect, useMemo, useRef, useState } from "react";
import { JimboBadge, JimboButton, JimboPanel, JimboStatusPill, JimboText } from "jaml-ui";
import { JimboErrorBlock, JimboSectionHeader, JimboTextInput } from "jaml-ui/ui";
import {
  DEFAULT_AGUI_ENDPOINT,
  deriveMcpEndpoint,
  isInsecureRemoteToken,
  normalizeAguiEndpoint,
  probeAguiServer,
  runAgent,
  type AguiProbeResult,
  type AguiProgress,
} from "./agui.js";

// The SIMD Ripper panel: connects directly to Motely.MCP's /agui endpoint — either same-origin
// when Motely.MCP hosts Motely.Web/dist directly, or over authenticated HTTPS for remote/local
// agents. Bearer tokens are kept in sessionStorage only (never localStorage or URL query params).

/** Seeds are 0 to 8 characters from 1-9 and A-Z (with the 0-char seed streamed as '""'). */
const SEED_RE = /^(?:""|[1-9A-Z]{1,8})$/;
const ENDPOINT_STORAGE_KEY = "motely.mcp.endpoint";
const TOKEN_SESSION_KEY = "motely.mcp.token";

const numberFmt = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

type RunStatus = "running" | "done" | "stopped" | "error";

interface RunRecord {
  prompt: string;
  status: RunStatus;
  /** Assistant narration lines (everything that isn't a bare seed). */
  narration: string[];
  /** Bare-seed text messages, in arrival order — the live match feed. */
  seeds: string[];
  progress: AguiProgress | null;
  /** Transport failure (server down / auth error) or the agent's RUN_ERROR message. */
  error?: string;
}

function readLocal(key: string): string {
  try {
    return window.localStorage.getItem(key) ?? "";
  } catch {
    return "";
  }
}

function writeLocal(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Ignore storage refusal in private browsing.
  }
}

function readSession(key: string): string {
  try {
    return window.sessionStorage.getItem(key) ?? "";
  } catch {
    return "";
  }
}

function writeSession(key: string, value: string): void {
  try {
    if (value) window.sessionStorage.setItem(key, value);
    else window.sessionStorage.removeItem(key);
  } catch {
    // Ignore storage refusal in private browsing.
  }
}

export interface AgentPanelProps {
  onInspect?: (seed: string) => void;
  onOpenPile?: (seeds: string[]) => void;
  initialInput?: string;
}

export function AgentPanel({ onInspect, onOpenPile, initialInput = "" }: AgentPanelProps = {}) {
  const [endpoint, setEndpoint] = useState<string>(
    () => readLocal(ENDPOINT_STORAGE_KEY) || DEFAULT_AGUI_ENDPOINT,
  );
  const [token, setToken] = useState<string>(() => readSession(TOKEN_SESSION_KEY));
  const [showToken, setShowToken] = useState(false);
  const [probe, setProbe] = useState<AguiProbeResult | null>(null);
  const [probing, setProbing] = useState(false);
  const [showSetupGuide, setShowSetupGuide] = useState(false);
  const [copiedSnippet, setCopiedSnippet] = useState<string | null>(null);

  const [runs, setRuns] = useState<RunRecord[]>([]);
  const [input, setInput] = useState(initialInput);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (initialInput && !input) {
      setInput(initialInput);
    }
  }, [initialInput]);

  // One thread per page load: every run continues the same conversation, per the AG-UI spec.
  const [threadId] = useState(() => crypto.randomUUID().replaceAll("-", ""));
  const abortRef = useRef<AbortController | null>(null);
  const logRef = useRef<HTMLDivElement | null>(null);

  const active = busy ? runs[runs.length - 1] : null;
  useEffect(() => {
    const el = logRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [runs, busy]);

  const endpointCheck = useMemo(() => normalizeAguiEndpoint(endpoint), [endpoint]);
  const pageOrigin = typeof window !== "undefined" ? window.location.origin : "http://localhost:5200";
  const pageProtocol = typeof window !== "undefined" ? window.location.protocol : "http:";
  const mcpUrl = useMemo(() => deriveMcpEndpoint(endpoint, pageOrigin), [endpoint, pageOrigin]);
  const insecureTokenWarning = useMemo(
    () => isInsecureRemoteToken(endpoint, token, pageProtocol),
    [endpoint, token, pageProtocol],
  );

  const mcpClientConfigJson = useMemo(
    () =>
      JSON.stringify(
        {
          servers: {
            "motely-simd-ripper": {
              type: "http",
              url: mcpUrl,
              headers: {
                Authorization: "Bearer ${env:MOTELY_MCP_TOKEN}",
              },
            },
          },
        },
        null,
        2,
      ),
    [mcpUrl],
  );

  const hostSnippet = useMemo(
    () =>
      `// In Motely.MCP/Program.cs — host Motely.Web/dist + lock down /mcp & /agui
app.UseDefaultFiles();
app.UseStaticFiles(); // serve Motely.Web/dist copied into wwwroot/

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp") || ctx.Request.Path.StartsWithSegments("/agui"))
    {
        var expected = Environment.GetEnvironmentVariable("MOTELY_MCP_TOKEN");
        if (!string.IsNullOrEmpty(expected))
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(auth),
                Encoding.UTF8.GetBytes($"Bearer {expected}")))
            {
                ctx.Response.StatusCode = 401;
                return;
            }
        }
    }
    await next();
});
app.MapFallbackToFile("index.html");`,
    [],
  );

  function updateEndpoint(next: string) {
    setEndpoint(next);
    writeLocal(ENDPOINT_STORAGE_KEY, next);
    setProbe(null);
  }

  function updateToken(next: string) {
    setToken(next);
    writeSession(TOKEN_SESSION_KEY, next);
    setProbe(null);
  }

  async function testConnection() {
    setProbing(true);
    try {
      const result = await probeAguiServer({ endpoint, token });
      setProbe(result);
    } finally {
      setProbing(false);
    }
  }

  function copySnippet(id: string, text: string) {
    void navigator.clipboard?.writeText(text).then(
      () => {
        setCopiedSnippet(id);
        setTimeout(() => setCopiedSnippet((curr) => (curr === id ? null : curr)), 1400);
      },
      () => {},
    );
  }

  async function submit(prompt: string) {
    const text = prompt.trim();
    if (!text || busy) return;
    if (!endpointCheck.ok) {
      setProbe({ status: "offline", message: endpointCheck.error });
      return;
    }
    setInput("");
    setBusy(true);

    const record: RunRecord = { prompt: text, status: "running", narration: [], seeds: [], progress: null };
    setRuns((rs) => [...rs, record]);
    const index = runs.length;

    const patch = (p: Partial<RunRecord>) =>
      setRuns((rs) => rs.map((r, i) => (i === index ? { ...r, ...p } : r)));

    const history = [
      ...runs.flatMap((r) => [
        { role: "user" as const, content: r.prompt },
        { role: "assistant" as const, content: [...r.narration, ...r.seeds].join("\n") },
      ]),
      { role: "user" as const, content: text },
    ];

    const controller = new AbortController();
    abortRef.current = controller;
    try {
      await runAgent(
        { threadId, messages: history },
        {
          onText: (t) =>
            setRuns((rs) =>
              rs.map((r, i) =>
                i === index
                  ? SEED_RE.test(t)
                    ? { ...r, seeds: [...r.seeds, t] }
                    : { ...r, narration: [...r.narration, t] }
                  : r,
              ),
            ),
          onProgress: (progress) => patch({ progress }),
          onRunFinished: () => patch({ status: "done" }),
          onRunError: (message) => patch({ status: "error", error: message }),
        },
        controller.signal,
        { endpoint: endpointCheck.url, token },
      );
      setProbe({ status: "connected", message: `Connected to ${endpointCheck.url}.` });
      setRuns((rs) => {
        const r = rs[index];
        return r && r.status === "running" ? rs.map((x, i) => (i === index ? { ...x, status: "done" } : x)) : rs;
      });
    } catch (e) {
      if (controller.signal.aborted) {
        patch({ status: "stopped" });
      } else {
        const msg = e instanceof Error ? e.message : String(e);
        const isAuth = msg.includes("401") || msg.includes("403");
        patch({
          status: "error",
          error: isAuth
            ? msg
            : `Could not reach Motely.MCP at ${endpointCheck.url}. Start your SIMD Ripper (Motely.MCP) or check the endpoint URL and CORS/auth settings. (${msg})`,
        });
      }
    } finally {
      setBusy(false);
      abortRef.current = null;
    }
  }

  function stop() {
    abortRef.current?.abort();
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.8rem" }}>
      <JimboPanel>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: "0.5rem" }}>
          <JimboSectionHeader label="SIMD Ripper — Motely.MCP Direct Link" tone="purple" />
          <div style={{ display: "flex", alignItems: "center", gap: "0.4rem", flexWrap: "wrap" }}>
            {probe ? (
              <JimboStatusPill
                status={probe.status === "connected" ? "ok" : "error"}
                label={
                  probe.status === "connected"
                    ? "connected"
                    : probe.status === "unauthorized"
                      ? "401 auth required"
                      : probe.status === "forbidden"
                        ? "403 forbidden"
                        : "offline"
                }
              />
            ) : (
              <JimboBadge size="sm" tone="grey">
                ready to connect
              </JimboBadge>
            )}
            <JimboButton
              tone="blue"
              size="xs"
              disabled={probing || !endpointCheck.ok}
              onClick={() => void testConnection()}
            >
              {probing ? "Testing..." : "Test Connection"}
            </JimboButton>
            <JimboButton
              tone="orange"
              size="xs"
              onClick={() => setShowSetupGuide((v) => !v)}
            >
              {showSetupGuide ? "Hide Agent & Hosting Setup" : "Agent & Hosting Setup"}
            </JimboButton>
          </div>
        </div>

        <JimboText size="xs" tone="grey">
          Built to be hosted directly by <code>Motely.MCP</code> on your SIMD machine (same-origin{" "}
          <code>/agui</code> + <code>/mcp</code>) or connected remotely over HTTPS with Bearer auth.
          Runs the native multi-threaded SIMD engine — no LLM in the search loop.
        </JimboText>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
            gap: "0.6rem",
          }}
        >
          <label style={{ display: "flex", flexDirection: "column", gap: "0.25rem" }}>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "0.4rem" }}>
              <JimboText size="xs" tone="gold">
                Motely.MCP AG-UI Endpoint
              </JimboText>
              <div style={{ display: "flex", gap: "0.25rem" }}>
                <JimboButton tone="grey" size="xs" onClick={() => updateEndpoint("/agui")}>
                  Same-origin
                </JimboButton>
                <JimboButton tone="grey" size="xs" onClick={() => updateEndpoint("http://localhost:5200/agui")}>
                  Local :5200
                </JimboButton>
              </div>
            </div>
            <JimboTextInput
              value={endpoint}
              onChange={(e) => updateEndpoint(e.target.value)}
              placeholder="/agui or https://ripper.tailnet.ts.net/agui"
              aria-label="Motely.MCP AG-UI endpoint"
              spellCheck={false}
            />
          </label>

          <label style={{ display: "flex", flexDirection: "column", gap: "0.25rem" }}>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "0.4rem" }}>
              <JimboText size="xs" tone="gold">
                Bearer Token (session only)
              </JimboText>
              <div style={{ display: "flex", gap: "0.25rem" }}>
                <JimboButton tone="grey" size="xs" onClick={() => setShowToken((v) => !v)}>
                  {showToken ? "Hide" : "Show"}
                </JimboButton>
                {token ? (
                  <JimboButton tone="red" size="xs" onClick={() => updateToken("")}>
                    Clear
                  </JimboButton>
                ) : null}
              </div>
            </div>
            <JimboTextInput
              type={showToken ? "text" : "password"}
              value={token}
              onChange={(e) => updateToken(e.target.value)}
              placeholder="Optional — sent as Authorization: Bearer <token>"
              aria-label="Bearer token"
              autoComplete="off"
              spellCheck={false}
            />
          </label>
        </div>

        {!endpointCheck.ok ? (
          <JimboErrorBlock title="Invalid endpoint">{endpointCheck.error}</JimboErrorBlock>
        ) : null}

        {insecureTokenWarning ? (
          <JimboErrorBlock title="Insecure transport warning">
            You entered a Bearer token for a non-localhost HTTP endpoint. Put Motely.MCP behind
            HTTPS (Tailscale Serve, Caddy, or Cloudflare Tunnel) before sending tokens off-box.
          </JimboErrorBlock>
        ) : null}

        {probe && probe.status !== "connected" ? (
          <JimboText size="xs" tone="red">
            {probe.message}
          </JimboText>
        ) : probe ? (
          <JimboText size="xs" tone="green">
            {probe.message}
          </JimboText>
        ) : null}

        {showSetupGuide ? (
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "0.6rem",
              padding: "0.7rem",
              borderRadius: 8,
              border: "1px solid var(--j-inner-border)",
              background: "var(--j-surface-inset)",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "0.5rem", flexWrap: "wrap" }}>
              <JimboText size="xs" tone="gold">
                1. Connect Local / Authenticated Agents directly to MCP ({mcpUrl})
              </JimboText>
              <JimboButton
                tone={copiedSnippet === "mcp" ? "green" : "blue"}
                size="xs"
                onClick={() => copySnippet("mcp", mcpClientConfigJson)}
              >
                {copiedSnippet === "mcp" ? "Copied!" : "Copy mcp.json"}
              </JimboButton>
            </div>
            <pre
              style={{
                margin: 0,
                padding: "0.55rem",
                borderRadius: 6,
                background: "var(--j-darkest)",
                color: "var(--j-white)",
                fontSize: "0.75rem",
                overflowX: "auto",
              }}
            >
              {mcpClientConfigJson}
            </pre>

            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "0.5rem", flexWrap: "wrap" }}>
              <JimboText size="xs" tone="gold">
                2. Host Motely.Web/dist directly inside Motely.MCP (one binary, same-origin + fixed-time Bearer auth)
              </JimboText>
              <JimboButton
                tone={copiedSnippet === "host" ? "green" : "blue"}
                size="xs"
                onClick={() => copySnippet("host", hostSnippet)}
              >
                {copiedSnippet === "host" ? "Copied!" : "Copy C# Snippet"}
              </JimboButton>
            </div>
            <pre
              style={{
                margin: 0,
                padding: "0.55rem",
                borderRadius: 6,
                background: "var(--j-darkest)",
                color: "var(--j-white)",
                fontSize: "0.75rem",
                overflowX: "auto",
              }}
            >
              {hostSnippet}
            </pre>
          </div>
        ) : null}
      </JimboPanel>

      <JimboPanel>
        <JimboText size="xs" tone="grey">
          Name any filter from <code>Filters/</code>, with or without <code>search</code>: try{" "}
          <code>boss-the-eye</code>, <code>Zerkeo_Pure keyword COLA</code>, or{" "}
          <code>longColaDanker random 500</code>.
        </JimboText>

        {runs.length === 0 ? (
          <JimboText size="sm" tone="grey">
            No runs yet — enter a filter command below to drive your SIMD Ripper.
          </JimboText>
        ) : (
          <div
            ref={logRef}
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "0.9rem",
              maxHeight: "55vh",
              overflowY: "auto",
              paddingRight: "0.3rem",
            }}
          >
            {runs.map((run, i) => (
              <RunCard
                key={i}
                run={run}
                live={run === active}
                onInspect={onInspect}
                onOpenPile={onOpenPile}
              />
            ))}
          </div>
        )}

        <form
          style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}
          onSubmit={(e) => {
            e.preventDefault();
            void submit(input);
          }}
        >
          <div style={{ flex: 1 }}>
            <JimboTextInput
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder='search boss-the-eye — or "Zerkeo_Pure keyword COLA"'
              disabled={busy}
              style={{ width: "100%" }}
              aria-label="Filter to search"
            />
          </div>
          {busy ? (
            <JimboButton tone="red" onClick={stop}>
              Stop
            </JimboButton>
          ) : (
            <JimboButton tone="green" type="submit" disabled={!input.trim() || !endpointCheck.ok}>
              Run on Ripper
            </JimboButton>
          )}
        </form>
      </JimboPanel>
    </div>
  );
}

function RunCard({
  run,
  live,
  onInspect,
  onOpenPile,
}: {
  run: RunRecord;
  live: boolean;
  onInspect?: (seed: string) => void;
  onOpenPile?: (seeds: string[]) => void;
}) {
  const cleanSeeds = run.seeds.map((s) => (s === '""' ? "" : s)).filter((s) => s.length > 0);
  return (
    <div
      style={{
        border: "1px solid var(--j-inner-border)",
        borderRadius: 8,
        padding: "0.7rem 0.9rem",
        display: "flex",
        flexDirection: "column",
        gap: "0.55rem",
        background: "var(--j-surface-inset)",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: "0.6rem", flexWrap: "wrap" }}>
        <JimboText size="sm" tone="white">
          {run.prompt}
        </JimboText>
        <JimboStatusPill
          status={run.status === "running" ? "running" : run.status === "done" ? "ok" : "error"}
          label={live ? "running" : run.status}
        />
        {onOpenPile && cleanSeeds.length > 0 ? (
          <JimboButton tone="green" size="xs" onClick={() => onOpenPile(cleanSeeds)}>
            Open {cleanSeeds.length} in Seed Pile
          </JimboButton>
        ) : null}
      </div>

      {run.progress ? <ProgressRow progress={run.progress} done={run.status !== "running"} /> : null}

      {run.narration.map((line, i) => (
        <JimboText key={i} size="sm" tone="grey">
          {line}
        </JimboText>
      ))}

      {run.seeds.length > 0 ? (
        <div style={{ display: "flex", flexWrap: "wrap", gap: "0.3rem" }}>
          {run.seeds.map((seed) => (
            <SeedChip key={seed} seed={seed} onInspect={onInspect} />
          ))}
        </div>
      ) : null}

      {run.error ? <JimboErrorBlock title="Run failed">{run.error}</JimboErrorBlock> : null}
    </div>
  );
}

function ProgressRow({ progress, done }: { progress: AguiProgress; done: boolean }) {
  const pct = Math.min(100, Math.max(0, progress.percent));
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.3rem" }}>
      <div
        role="progressbar"
        aria-valuenow={Math.round(pct)}
        style={{
          height: 10,
          borderRadius: 5,
          background: "var(--j-darkest)",
          border: "1px solid var(--j-inner-border)",
          overflow: "hidden",
        }}
      >
        <div
          style={{
            width: `${pct}%`,
            height: "100%",
            background: done ? "var(--j-green)" : "var(--j-blue)",
            transition: "width 0.2s ease",
          }}
        />
      </div>
      <JimboText size="xs" tone="grey">
        {numberFmt.format(progress.searched)} searched · {numberFmt.format(progress.matched)} matched
        · {numberFmt.format(Math.round(progress.rate))} seeds/s
        {progress.elapsed ? ` · ${progress.elapsed}` : ""}
      </JimboText>
    </div>
  );
}

function SeedChip({ seed, onInspect }: { seed: string; onInspect?: (seed: string) => void }) {
  const [copied, setCopied] = useState(false);
  const rawSeed = seed === '""' ? "" : seed;
  const displaySeed = rawSeed === "" ? '""' : rawSeed;
  return (
    <div style={{ display: "inline-flex", alignItems: "center", gap: "0.25rem" }}>
      <JimboButton
        size="xs"
        tone={copied ? "green" : "blue"}
        title={`Click to copy ${displaySeed}`}
        onClick={() => {
          void navigator.clipboard?.writeText(rawSeed).then(
            () => {
              setCopied(true);
              setTimeout(() => setCopied(false), 1200);
            },
            () => {},
          );
        }}
      >
        {copied ? "copied!" : displaySeed}
      </JimboButton>
      <JimboButton
        size="xs"
        tone="grey"
        title={`Inspect ${displaySeed} in Seed Lab`}
        onClick={() => {
          if (onInspect) {
            onInspect(rawSeed);
          } else if (typeof window !== "undefined") {
            const url = new URL(window.location.href);
            url.searchParams.set("tab", "lab");
            url.searchParams.set("seed", rawSeed);
            window.location.href = url.toString();
          }
        }}
      >
        inspect
      </JimboButton>
    </div>
  );
}
