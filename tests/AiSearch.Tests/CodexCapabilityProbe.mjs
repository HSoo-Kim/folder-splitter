// Explicit developer diagnostic. Inference is captured by a synthetic loopback server.
// --catalog/--account-catalog use subscription authentication for official metadata only.
// Credentials are removed before the synthetic provider starts.
// Run: bun tests/AiSearch.Tests/CodexCapabilityProbe.mjs <absolute-codex-executable>
import { mkdtemp, mkdir, rm, copyFile, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const root = await mkdtemp(join(tmpdir(), "quadexplorer-codex-wire-"));
const home = join(root, "config");
const work = join(root, "work");
await mkdir(home);
await mkdir(work);
let process;
let catalogPath;
if (Bun.argv.includes("--catalog") || Bun.argv.includes("--account-catalog")) {
  const installed = Bun.env.CODEX_HOME || join(Bun.env.USERPROFILE, ".codex");
  const auth = JSON.parse(await readFile(join(installed, "auth.json"), "utf8"));
  if (!auth.tokens || auth.auth_mode && auth.auth_mode !== "chatgpt")
    throw new Error("A ChatGPT subscription login is required; API-key credentials are not used.");
  await copyFile(join(installed, "auth.json"), join(home, "auth.json"));
  let catalog;
  let catalogChild;
  let catalogDeadline;
  try {
    const env = { ...Bun.env, CODEX_HOME: home };
    for (const key of ["OPENAI_API_KEY", "CODEX_API_KEY", "OPENAI_BASE_URL", "RUST_LOG"]) delete env[key];
    const child = catalogChild = Bun.spawn([Bun.argv[2], "debug", "models",
      "-c", 'model_provider="openai"', "-c", 'cli_auth_credentials_store="file"'], {
      cwd: work, env, stdout: "pipe", stderr: "pipe"
    });
    catalogDeadline = setTimeout(() => child.kill(), 20000);
    const output = new Response(child.stdout).text();
    const error = new Response(child.stderr).text();
    const code = await child.exited;
    catalog = JSON.parse(await output);
    console.log("CATALOG_SHAPE " + JSON.stringify(Object.keys(catalog)));
    const models = Array.isArray(catalog) ? catalog : catalog.models;
    for (const model of models || []) console.log(JSON.stringify({
      id: model.slug || model.id, name: model.display_name, shell: model.shell_type,
      patch: model.apply_patch_tool_type, effort: model.supported_reasoning_levels,
      visibility: model.visibility, available: model.available_in_plans,
      capabilityKeys: Object.keys(model).filter(x => /tool|code|response|search/.test(x)),
      tool_mode: model.tool_mode, use_responses_lite: model.use_responses_lite,
      experimental_supported_tools: model.experimental_supported_tools
    }));
    console.log("CATALOG_EXIT " + code + " " + (await error).slice(-1500));
  } catch (error) {
    catalogChild?.kill();
    if (catalogChild) await catalogChild.exited;
    await rm(root, { recursive: true, force: true });
    throw error;
  } finally {
    clearTimeout(catalogDeadline);
    await rm(join(home, "auth.json"), { force: true });
  }
  if (Bun.argv.includes("--catalog")) {
    await rm(root, { recursive: true, force: true });
    globalThis.process.exit(0);
  }
  catalogPath = join(root, "catalog.json");
  if (Bun.argv.includes("--model-only"))
    for (const model of catalog.models) {
      model.tool_mode = "direct";
      model.shell_type = "disabled";
      model.apply_patch_tool_type = null;
      model.experimental_supported_tools = [];
      model.supports_search_tool = false;
      model.use_responses_lite = false;
    }
  await writeFile(catalogPath, JSON.stringify(catalog));
}
let capture;
const received = new Promise(resolve => { capture = resolve; });
const server = Bun.serve({
  hostname: "127.0.0.1",
  port: 0,
  async fetch(request) {
    const body = await request.json();
    capture(body);
    return new Response("event: response.completed\ndata: " + JSON.stringify({
      type: "response.completed",
      response: { id: "synthetic", status: "completed", output: [], usage: {
        input_tokens: 0, output_tokens: 0, total_tokens: 0
      } }
    }) + "\n\n", { headers: { "content-type": "text/event-stream" } });
  }
});
try {
  const disabled = ["shell_tool", "view_image", "multi_agent", "multi_agent_v2", "apps", "plugins",
    "remote_plugin", "hooks", "shell_snapshot", "memories", "browser_use",
    "computer_use", "image_generation", "code_mode", "code_mode_host",
    "tool_suggest", "sleep_tool", "goals", "workspace_dependencies", "executor_capability_discovery"];
  const config = [
    'model_provider="synthetic"',
    'model_providers.synthetic.name="Synthetic local wire probe"',
    `model_providers.synthetic.base_url="http://127.0.0.1:${server.port}/v1"`,
    'model_providers.synthetic.wire_api="responses"',
    'model_providers.synthetic.requires_openai_auth=false',
    'model_providers.synthetic.supports_websockets=false',
    'model_providers.synthetic.request_max_retries=0',
    'model_providers.synthetic.stream_max_retries=0',
    'web_search="disabled"', "project_doc_max_bytes=0",
    "tools.update_plan.enabled=false", "tools.experimental_request_user_input.enabled=false",
    'history.persistence="none"', "mcp_servers={}", "agents.enabled=false",
    "features.skip_host_skill_discovery=true", "skills.include_instructions=false",
    "skills.bundled.enabled=false",
  ];
  if (catalogPath) config.push(`model_catalog_json=${JSON.stringify(catalogPath.replaceAll("\\", "/"))}`);
  const rpc = Bun.argv.includes("--app-server");
  const command = rpc ? ["app-server", "--stdio"] : ["exec", "--ignore-user-config", "--ignore-rules",
    "--skip-git-repo-check", "--ephemeral", "--json", "--sandbox", "read-only",
    "--model", catalogPath ? "gpt-6-astra" : "gpt-6.1-sol"];
  process = Bun.spawn([Bun.argv[2], ...command, ...disabled.flatMap(x => ["--disable", x]),
    ...config.flatMap(x => ["-c", x]), ...rpc ? [] : ["Return only OK. Do not use tools."]], {
    cwd: work, env: { ...Bun.env, CODEX_HOME: home, OPENAI_API_KEY: "",
      CODEX_API_KEY: "", TMP: root, TEMP: root, TMPDIR: root },
    stdin: "pipe", stdout: "pipe", stderr: "pipe"
  });
  const send = value => { process.stdin.write(JSON.stringify(value) + "\n"); process.stdin.flush(); };
  const stdout = rpc ? (async () => {
    let pending = "";
    for await (const bytes of process.stdout) {
      pending += new TextDecoder().decode(bytes);
      let end;
      while ((end = pending.indexOf("\n")) >= 0) {
        const line = pending.slice(0, end);
        pending = pending.slice(end + 1);
        if (!line) continue;
        const message = JSON.parse(line);
        if (message.error) console.log("RPC_ERROR " + JSON.stringify(message.error));
        if (message.id === 1 && message.result) {
          send({ method: "initialized" });
          send({ id: 2, method: "thread/start", params: {
            model: "gpt-6-astra", modelProvider: "synthetic", cwd: work, ephemeral: true,
            environments: [], dynamicTools: [], selectedCapabilityRoots: [], runtimeWorkspaceRoots: [],
            sandbox: "read-only", approvalPolicy: "never",
            baseInstructions: "Return only requested text.", developerInstructions: "Do not use tools."
          } });
        }
        if (message.id === 2 && message.result) send({ id: 3, method: "turn/start", params: {
          threadId: message.result.thread.id, input: [{ type: "text", text: "Return only OK." }]
        } });
      }
    }
  })() : new Response(process.stdout).text();
  if (rpc) send({ id: 1, method: "initialize", params: {
    clientInfo: { name: "quadexplorer_probe", version: "1" }, capabilities: { experimentalApi: true }
  } });
  else process.stdin.end();
  const stderr = new Response(process.stderr).text();
  let timer;
  const result = await Promise.race([
    received.then(body => ({ body })),
    process.exited.then(async code => ({ code, error: await stderr })),
    new Promise(resolve => { timer = setTimeout(() => resolve({ timeout: true }), 20000); })
  ]);
  clearTimeout(timer);
  if (result.body) {
    const tools = [...result.body.tools || [],
      ...(result.body.input || []).flatMap(x => x.type === "additional_tools" ? x.tools || [] : [])];
    console.log(JSON.stringify({ model: result.body.model, tools: tools.map(x => ({
      type: x.type, name: x.name, tools: x.tools?.map(t => t.name)
    })),
      keys: Object.keys(result.body),
      toolMentions: [...new Set(JSON.stringify(result.body).match(/exec_command|apply_patch|write_stdin|view_image|tool_definitions/g) || [])],
      inputRoles: result.body.input?.map(x => x.role || x.type) }, null, 2));
  } else console.log(JSON.stringify(result));
  process.kill();
  await process.exited;
  console.log("STDERR " + (await stderr).slice(-6000));
  await stdout;
} finally {
  process?.kill();
  if (process) await process.exited;
  server.stop(true);
  await rm(root, { recursive: true, force: true });
}
