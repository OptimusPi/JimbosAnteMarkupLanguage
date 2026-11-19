import { createRoot } from "react-dom/client";
import bootsharp from "motely-wasm";
import "jaml-ui/jimbo.css";
import { App } from "./App.js";

await bootsharp.boot();

createRoot(document.getElementById("root")!).render(<App folders={false} />);
