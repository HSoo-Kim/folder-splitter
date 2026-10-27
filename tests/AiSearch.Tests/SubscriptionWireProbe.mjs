// Explicit developer diagnostic: synthetic prompts only, no credential/config writes.
import { readFile } from "node:fs/promises";
import { join } from "node:path";

const auth = JSON.parse(await readFile(join(process.env.CODEX_HOME || join(process.env.USERPROFILE, ".codex"), "auth.json"), "utf8"));
if (auth.auth_mode !== "chatgpt" || !auth.tokens?.access_token) {
  throw new Error("ChatGPT subscription login required.");
}
const headers = {
  Authorization: `Bearer ${auth.tokens.access_token}`,
  "Content-Type": "application/json",
  "User-Agent": "FolderSplitter/0.8.9"
};
if (auth.tokens.account_id) headers["ChatGPT-Account-ID"] = auth.tokens.account_id;
const base = "https://chatgpt.com/backend-api/codex";
const catalog = await fetch(`${base}/models?client_version=0.162.1`, {
  headers, redirect: "error", signal: AbortSignal.timeout(30000)
});
if (!catalog.ok) {
  console.log(`SUBSCRIPTION_CATALOG status=${catalog.status}`);
  process.exitCode = 1;
} else {
  const metadata = await catalog.json();
  console.log("SUBSCRIPTION_MODELS " + JSON.stringify(metadata.models.map(model => ({
    id: model.slug, name: model.display_name, visibility: model.visibility,
    ...(process.argv.includes("--usage") ? {
      contextWindow: model.context_window,
      maxContextWindow: model.max_context_window,
      effectiveContextWindowPercent: model.effective_context_window_percent
    } : {})
  }))));
  for (const model of process.argv.includes("--luna-only") ? ["gpt-6-luna"] : ["gpt-6.1-sol", "gpt-6-luna", "gpt-6-astra"]) {
    const started = performance.now();
    const response = await fetch(`${base}/responses`, {
      method: "POST", headers, redirect: "error", signal: AbortSignal.timeout(75000),
      body: JSON.stringify({
        model, store: false, stream: true, tools: [], tool_choice: "none",
        ...(process.argv.includes("--json-object") ? { text: { format: { type: "json_object" } } } : {}),
        ...(process.argv.includes("--json-schema") ? { text: { format: {
          type: "json_schema", name: "probe_result", strict: true,
          schema: { type: "object", properties: { ok: { type: "boolean" } }, required: ["ok"], additionalProperties: false }
        } } } : {}),
        instructions: "Return only the requested JSON object. No tools.",
        input: [{ role: "user", content: [{ type: "input_text", text: 'Return exactly {"ok":true}.' }] }]
      })
    });
    if (!response.ok) {
      const error = await response.json();
      console.log("SUBSCRIPTION_MODEL " + JSON.stringify({
        model, status: response.status, detail: error.error?.message || error.detail
      }));
      continue;
    }
    let output = "", pending = "", total = 0, completed = false, toolCall = false, usage;
    const decoder = new TextDecoder();
    for await (const bytes of response.body) {
      total += bytes.byteLength;
      if (total > 1048576) throw new Error("Synthetic response exceeds limit.");
      pending += decoder.decode(bytes, { stream: true });
      let end;
      while ((end = pending.indexOf("\n")) >= 0) {
        const line = pending.slice(0, end).trim();
        pending = pending.slice(end + 1);
        if (!line.startsWith("data:") || line === "data: [DONE]") continue;
        const event = JSON.parse(line.slice(5).trim());
        if (event.type === "response.output_text.delta") output += event.delta;
        if (event.type === "response.completed") {
          completed = true;
          if (process.argv.includes("--usage")) usage = event.response?.usage;
        }
        if (event.item?.type?.includes("call")) toolCall = true;
        if (event.type === "error" || event.type === "response.failed")
          console.log("SUBSCRIPTION_EVENT_ERROR " + JSON.stringify({
            model, type: event.type, code: event.code, message: event.message?.slice(0, 500)
          }));
      }
    }
    console.log("SUBSCRIPTION_MODEL " + JSON.stringify({
      model, status: response.status, completed, toolCall,
      exact: output.trim() === '{"ok":true}', milliseconds: Math.round(performance.now() - started),
      ...(process.argv.includes("--usage") ? { usage } : {})
    }));
    if (!completed || toolCall || output.trim() !== '{"ok":true}') process.exitCode = 1;
  }
}
