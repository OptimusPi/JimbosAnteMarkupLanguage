import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  DEFAULT_AGUI_ENDPOINT,
  deriveMcpEndpoint,
  isInsecureRemoteToken,
  normalizeAguiEndpoint,
  probeAguiServer,
  runAgent,
} from "./agui.ts";

describe("agui endpoint & security helpers", () => {
  it("normalizes blank, relative, and http(s) endpoints and rejects unsafe schemes", () => {
    assert.deepEqual(normalizeAguiEndpoint(""), { ok: true, url: DEFAULT_AGUI_ENDPOINT });
    assert.deepEqual(normalizeAguiEndpoint("/agui"), { ok: true, url: "/agui" });
    assert.deepEqual(normalizeAguiEndpoint("http://localhost:5200"), {
      ok: true,
      url: "http://localhost:5200/agui",
    });
    assert.deepEqual(normalizeAguiEndpoint("https://ripper.tailnet.ts.net/custom/agui"), {
      ok: true,
      url: "https://ripper.tailnet.ts.net/custom/agui",
    });

    assert.equal(normalizeAguiEndpoint("javascript:alert(1)").ok, false);
    assert.equal(normalizeAguiEndpoint("//evil.example/agui").ok, false);
    assert.equal(normalizeAguiEndpoint("not a url").ok, false);
  });

  it("derives the sibling /mcp endpoint for agent configs", () => {
    assert.equal(deriveMcpEndpoint("/agui", "https://ripper.example"), "https://ripper.example/mcp");
    assert.equal(deriveMcpEndpoint("http://localhost:5200/agui"), "http://localhost:5200/mcp");
    assert.equal(deriveMcpEndpoint("https://ripper.tailnet.ts.net"), "https://ripper.tailnet.ts.net/mcp");
  });

  it("flags Bearer tokens sent over plain HTTP to non-loopback hosts", () => {
    assert.equal(isInsecureRemoteToken("http://192.168.1.50:5200/agui", "secret-token"), true);
    assert.equal(isInsecureRemoteToken("https://ripper.tailnet.ts.net/agui", "secret-token"), false);
    assert.equal(isInsecureRemoteToken("http://localhost:5200/agui", "secret-token"), false);
    assert.equal(isInsecureRemoteToken("http://127.0.0.1:5200/agui", "secret-token"), false);
    assert.equal(isInsecureRemoteToken("http://192.168.1.50:5200/agui", ""), false);
  });
});

describe("agui network auth & SSE streaming", () => {
  it("sends Authorization: Bearer header and classifies 401/405 in probeAguiServer", async () => {
    const seenHeaders: Record<string, string>[] = [];
    const originalFetch = globalThis.fetch;
    try {
      globalThis.fetch = (async (_url, init) => {
        seenHeaders.push((init?.headers as Record<string, string>) ?? {});
        const auth = (init?.headers as Record<string, string> | undefined)?.Authorization;
        if (auth !== "Bearer ripper-secret") {
          return new Response("Unauthorized", { status: 401 });
        }
        return new Response(null, { status: 405 });
      }) as typeof fetch;

      const unauth = await probeAguiServer({ endpoint: "https://ripper.example/agui" });
      assert.equal(unauth.status, "unauthorized");
      assert.equal(unauth.httpStatus, 401);

      const authed = await probeAguiServer({
        endpoint: "https://ripper.example/agui",
        token: "ripper-secret",
      });
      assert.equal(authed.status, "connected");
      assert.equal(authed.httpStatus, 405);
      assert.equal(seenHeaders[1].Authorization, "Bearer ripper-secret");
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it("streams SSE progress + text messages with Bearer auth in runAgent", async () => {
    const originalFetch = globalThis.fetch;
    let receivedAuth = "";
    let receivedUrl = "";
    try {
      globalThis.fetch = (async (url, init) => {
        receivedUrl = String(url);
        receivedAuth = (init?.headers as Record<string, string> | undefined)?.Authorization ?? "";
        const payload = [
          `data: ${JSON.stringify({ type: "RUN_STARTED", threadId: "t1", runId: "r1" })}\n\n`,
          `data: ${JSON.stringify({ type: "STATE_SNAPSHOT", snapshot: { percent: 50, searched: 100000, matched: 1, rate: 250000 } })}\n\n`,
          `data: ${JSON.stringify({ type: "TEXT_MESSAGE_START", messageId: "m1" })}\n\n`,
          `data: ${JSON.stringify({ type: "TEXT_MESSAGE_CONTENT", messageId: "m1", delta: "ALEEB" })}\n\n`,
          `data: ${JSON.stringify({ type: "TEXT_MESSAGE_END", messageId: "m1" })}\n\n`,
          `data: ${JSON.stringify({ type: "RUN_FINISHED", threadId: "t1", runId: "r1" })}\n\n`,
        ].join("");
        const stream = new ReadableStream<Uint8Array>({
          start(controller) {
            controller.enqueue(new TextEncoder().encode(payload));
            controller.close();
          },
        });
        return new Response(stream, { status: 200, headers: { "Content-Type": "text/event-stream" } });
      }) as typeof fetch;

      const texts: string[] = [];
      let matched = 0;
      let finished = false;

      await runAgent(
        { threadId: "t1", messages: [{ role: "user", content: "boss-the-eye" }] },
        {
          onText: (t) => texts.push(t),
          onProgress: (p) => {
            matched = p.matched;
          },
          onRunFinished: () => {
            finished = true;
          },
        },
        undefined,
        { endpoint: "https://ripper.example/agui", token: "tok123" },
      );

      assert.equal(receivedUrl, "https://ripper.example/agui");
      assert.equal(receivedAuth, "Bearer tok123");
      assert.deepEqual(texts, ["ALEEB"]);
      assert.equal(matched, 1);
      assert.equal(finished, true);
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
});
