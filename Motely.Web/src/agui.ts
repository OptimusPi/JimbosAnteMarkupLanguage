// Minimal AG-UI client for Motely.MCP's /agui endpoint (AGUI.Server's canonical SSE
// transport). Supports both same-origin hosting (when Motely.MCP serves Motely.Web directly)
// and remote authenticated connections with Bearer tokens for SIMD Ripper instances.
//
// Deliberately React-free — only fetch + SSE parsing — so node tests can import it directly.

export interface AguiInputMessage {
  role: "user" | "assistant" | "system" | "tool";
  content: string;
}

export interface RunAgentInput {
  threadId?: string;
  runId?: string;
  messages: AguiInputMessage[];
}

export interface AguiProgress {
  percent: number;
  searched: number;
  matched: number;
  rate: number;
  /** Present on the final snapshot: the run's formatted duration. */
  elapsed?: string;
}

export interface AguiConnectionConfig {
  /** AG-UI endpoint path or full URL. Defaults to same-origin "/agui". */
  endpoint: string;
  /** Optional Bearer token sent via `Authorization: Bearer <token>`. Never persisted to localStorage. */
  token?: string;
}

export type AguiProbeStatus = "connected" | "unauthorized" | "forbidden" | "offline";

export interface AguiProbeResult {
  status: AguiProbeStatus;
  httpStatus?: number;
  message: string;
}

export interface AguiHandlers {
  onRunStarted?(threadId: string, runId: string): void;
  /** One complete assistant text message (TEXT_MESSAGE_START/CONTENT/END assembled). */
  onText?(text: string): void;
  /** A STATE_SNAPSHOT event — the agent's live progress widget state. */
  onProgress?(progress: AguiProgress): void;
  onRunFinished?(threadId: string, runId: string): void;
  onRunError?(message: string): void;
}

export const DEFAULT_AGUI_ENDPOINT = "/agui";

/**
 * Normalizes a user-entered AG-UI endpoint.
 * - Blank -> "/agui" (same-origin, when Motely.Web is hosted by Motely.MCP)
 * - Relative path starting with "/" -> kept as-is
 * - http:// or https:// URL -> appends "/agui" if no path was given
 * - Any other scheme (javascript:, data:, file:, etc.) -> rejected
 */
export function normalizeAguiEndpoint(
  raw: string,
): { ok: true; url: string } | { ok: false; error: string } {
  const trimmed = raw.trim();
  if (!trimmed) return { ok: true, url: DEFAULT_AGUI_ENDPOINT };
  if (trimmed.startsWith("/")) {
    if (trimmed.startsWith("//")) {
      return { ok: false, error: "Protocol-relative URLs are not allowed. Use /agui or https://..." };
    }
    return { ok: true, url: trimmed };
  }
  try {
    const parsed = new URL(trimmed);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
      return { ok: false, error: `Unsupported protocol "${parsed.protocol}". Use http:// or https://.` };
    }
    if (!parsed.pathname || parsed.pathname === "/") {
      parsed.pathname = "/agui";
    }
    return { ok: true, url: parsed.toString() };
  } catch {
    return { ok: false, error: "Enter a relative path like /agui or a full http(s):// URL." };
  }
}

/** Derives the sibling MCP endpoint URL (e.g. /agui -> <origin>/mcp) for local/remote agent config snippets. */
export function deriveMcpEndpoint(aguiUrl: string, currentOrigin = "http://localhost:5200"): string {
  const normalized = normalizeAguiEndpoint(aguiUrl);
  const urlStr = normalized.ok ? normalized.url : DEFAULT_AGUI_ENDPOINT;
  try {
    const parsed = urlStr.startsWith("/") ? new URL(urlStr, currentOrigin) : new URL(urlStr);
    parsed.pathname = parsed.pathname.replace(/\/agui\/?$/i, "/mcp");
    if (!parsed.pathname.endsWith("/mcp")) {
      parsed.pathname = `${parsed.pathname.replace(/\/+$/, "")}/mcp`;
    }
    parsed.search = "";
    parsed.hash = "";
    return parsed.toString();
  } catch {
    return `${currentOrigin.replace(/\/+$/, "")}/mcp`;
  }
}

/** True when a Bearer token would be transmitted over unencrypted HTTP to a non-loopback host. */
export function isInsecureRemoteToken(endpoint: string, token: string | undefined, pageProtocol?: string): boolean {
  if (!token || !token.trim()) return false;
  const normalized = normalizeAguiEndpoint(endpoint);
  if (!normalized.ok) return false;
  if (normalized.url.startsWith("/")) {
    return pageProtocol === "http:" && !isLoopbackOrigin();
  }
  try {
    const parsed = new URL(normalized.url);
    if (parsed.protocol === "https:") return false;
    return !isLoopbackHost(parsed.hostname);
  } catch {
    return false;
  }
}

function isLoopbackHost(hostname: string): boolean {
  const host = hostname.toLowerCase();
  return host === "localhost" || host === "127.0.0.1" || host === "::1" || host === "[::1]";
}

function isLoopbackOrigin(): boolean {
  if (typeof window === "undefined") return true;
  return isLoopbackHost(window.location.hostname);
}

function buildHeaders(token: string | undefined, extra: Record<string, string> = {}): Record<string, string> {
  const headers: Record<string, string> = { ...extra };
  const cleanToken = token?.trim();
  if (cleanToken) {
    headers.Authorization = `Bearer ${cleanToken}`;
  }
  return headers;
}

/**
 * Probes whether the configured Motely.MCP AG-UI endpoint is reachable and whether auth succeeds.
 * AG-UI endpoints accept POST and typically return 405 Method Not Allowed on GET/HEAD after auth
 * middleware passes — so 200, 204, and 405 all confirm the server is live and authorized.
 */
export async function probeAguiServer(
  connection: AguiConnectionConfig,
  signal?: AbortSignal,
): Promise<AguiProbeResult> {
  const normalized = normalizeAguiEndpoint(connection.endpoint);
  if (!normalized.ok) {
    return { status: "offline", message: normalized.error };
  }
  try {
    const res = await fetch(normalized.url, {
      method: "HEAD",
      headers: buildHeaders(connection.token),
      signal,
    });
    if (res.status === 401) {
      return {
        status: "unauthorized",
        httpStatus: 401,
        message: "401 Unauthorized — provide a valid Bearer token for this Motely.MCP server.",
      };
    }
    if (res.status === 403) {
      return {
        status: "forbidden",
        httpStatus: 403,
        message: "403 Forbidden — this token does not have access to /agui.",
      };
    }
    if (res.ok || res.status === 405) {
      return {
        status: "connected",
        httpStatus: res.status,
        message: `Connected to ${normalized.url} (HTTP ${res.status}).`,
      };
    }
    if (res.status === 404) {
      return {
        status: "offline",
        httpStatus: 404,
        message: `HTTP 404 at ${normalized.url} — server responded, but /agui is not mapped here.`,
      };
    }
    return {
      status: "offline",
      httpStatus: res.status,
      message: `Unexpected HTTP ${res.status} from ${normalized.url}.`,
    };
  } catch (err) {
    return {
      status: "offline",
      message: `Cannot reach ${normalized.url} (${err instanceof Error ? err.message : String(err)}).`,
    };
  }
}

/** POSTs the run and consumes the SSE stream until the server closes it. Rejects on transport
 * errors and non-2xx responses (the caller turns that into UI); RUN_ERROR is delivered as an
 * onRunError event, not a rejection — it is still a completed run. */
export async function runAgent(
  input: RunAgentInput,
  handlers: AguiHandlers,
  signal?: AbortSignal,
  connection: AguiConnectionConfig = { endpoint: DEFAULT_AGUI_ENDPOINT },
): Promise<void> {
  const normalized = normalizeAguiEndpoint(connection.endpoint);
  if (!normalized.ok) {
    throw new Error(normalized.error);
  }

  const res = await fetch(normalized.url, {
    method: "POST",
    headers: buildHeaders(connection.token, {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
    }),
    body: JSON.stringify(input),
    signal,
  });
  if (!res.ok || !res.body) {
    if (res.status === 401) {
      throw new Error("HTTP 401 Unauthorized — set a valid Bearer token in SIMD Ripper connection settings.");
    }
    if (res.status === 403) {
      throw new Error("HTTP 403 Forbidden — token rejected by Motely.MCP.");
    }
    const detail = (await res.text().catch(() => "")).slice(0, 300);
    throw new Error(`HTTP ${res.status}${detail ? `: ${detail}` : ""}`);
  }

  // Text deltas accumulate per messageId so interleaved messages can't cross the streams.
  const textParts = new Map<string, string>();
  let textOrder: string[] = [];

  await consumeSse(res.body, signal, (evt) => {
    switch (evt.type) {
      case "RUN_STARTED":
        handlers.onRunStarted?.(str(evt.threadId), str(evt.runId));
        break;
      case "RUN_FINISHED":
        handlers.onRunFinished?.(str(evt.threadId), str(evt.runId));
        break;
      case "RUN_ERROR":
        handlers.onRunError?.(str(evt.message) || "run failed");
        break;
      case "TEXT_MESSAGE_START": {
        const id = str(evt.messageId);
        if (!textParts.has(id)) {
          textParts.set(id, "");
          textOrder.push(id);
        }
        break;
      }
      case "TEXT_MESSAGE_CONTENT":
        textParts.set(str(evt.messageId), (textParts.get(str(evt.messageId)) ?? "") + str(evt.delta));
        break;
      case "TEXT_MESSAGE_END": {
        const id = str(evt.messageId);
        const text = (textParts.get(id) ?? "").replace(/\n$/, "");
        textParts.delete(id);
        textOrder = textOrder.filter((x) => x !== id);
        if (text) handlers.onText?.(text);
        break;
      }
      case "STATE_SNAPSHOT":
        handlers.onProgress?.(parseProgress(evt.snapshot));
        break;
    }
  });
}

function parseProgress(snapshot: unknown): AguiProgress {
  const s = (snapshot ?? {}) as Record<string, unknown>;
  const num = (v: unknown) => (typeof v === "number" && Number.isFinite(v) ? v : 0);
  return {
    percent: num(s.percent),
    searched: num(s.searched),
    matched: num(s.matched),
    rate: num(s.rate),
    ...(typeof s.elapsed === "string" ? { elapsed: s.elapsed } : {}),
  };
}

function str(v: unknown): string {
  return typeof v === "string" ? v : "";
}

type SseEvent = Record<string, unknown> & { type?: string };

/** Reads an SSE body event-by-event: buffers chunks, splits on blank lines, joins the data
 * lines of each block and parses them as one JSON event. */
async function consumeSse(
  body: ReadableStream<Uint8Array>,
  signal: AbortSignal | undefined,
  onEvent: (evt: SseEvent) => void,
): Promise<void> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    // Events are separated by a blank line; a block's payload is its `data:` lines.
    let sep: number;
    while ((sep = buffer.search(/\r?\n\r?\n/)) !== -1) {
      const block = buffer.slice(0, sep);
      buffer = buffer.slice(buffer[sep] === "\r" ? sep + 4 : sep + 2);
      const data = block
        .split(/\r?\n/)
        .filter((l) => l.startsWith("data:"))
        .map((l) => l.slice(5).replace(/^ /, ""))
        .join("\n");
      if (!data) continue;
      try {
        const evt = JSON.parse(data) as SseEvent;
        if (evt && typeof evt === "object" && typeof evt.type === "string") onEvent(evt);
      } catch {
        // A partial/invalid block — the next one carries on; the stream is best-effort.
      }
    }
  }

  if (signal?.aborted) throw new DOMException("Aborted", "AbortError");
}
