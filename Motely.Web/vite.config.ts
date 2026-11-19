import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In production, Motely.MCP can serve Motely.Web/dist directly (same-origin /agui & /mcp).
// In dev, Vite proxies /agui and /mcp to your local Motely.MCP instance (default :5200).
const mcpUrl =
  process.env.services__mcp__http__0 ?? process.env.MOTELY_MCP_URL ?? "http://localhost:5200";

export default defineConfig({
  plugins: [react()],
  resolve: {
    dedupe: ["react", "react-dom", "motely-wasm"],
  },
  // jaml-ui finds its sprite sheets beside its own files. Pre-bundled for the dev server, those
  // paths point into .vite/deps, where no image is, and every card draws blank. Served as it
  // ships, the sheets load.
  optimizeDeps: { exclude: ["jaml-ui"] },
  server: {
    host: "0.0.0.0",
    port: Number(process.env.PORT) || 3000,
    strictPort: true,
    allowedHosts: true,
    fs: { allow: [".."] },
    proxy: {
      "/agui": { target: mcpUrl },
      "/mcp": { target: mcpUrl },
    },
  },
  build: {
    target: "esnext",
    // .NET's JavaScript probes for Node modules it never uses in a browser.
    rollupOptions: {
      external: ["process", "module"],
      output: {
        manualChunks(id) {
          if (id.includes("node_modules/motely-wasm")) return "motely-wasm";
          if (id.includes("node_modules/react") || id.includes("node_modules/react-dom")) {
            return "react-vendor";
          }
          return undefined;
        },
      },
    },
  },
});
