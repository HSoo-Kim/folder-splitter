# AI conversation search — feature branch beta

This document describes `feature/ai-search`, not the stable download. The branch
adds an optional AI side panel to the existing Windows application. Publishing
this branch does not publish a stable release or change the stable update feed.

## Use

- Toggle **AI search** on the global toolbar or press **Alt+I**.
- Open **AI search settings**, choose a provider, list/test its models, then select
  a model. The model menu groups providers and shows availability; a listed name
  does not guarantee that the signed-in account can use it. Reasoning choices
  depend on the selected model.
- Choose **Find by name** or **Search contents**, enter a request, and refine it in
  later messages. Switching modes starts a separate conversation.
- Results can be shown in the active pane, opened normally, opened in a new tab or
  pane, or previewed. Document answers include source paths, locators and supporting
  quotations. Select/copy transcript text separately from file-result cards.
- **New chat** clears the conversation; the send control can stop a pending request.
  The panel can be resized, detached into a window, and docked again.

## Providers and prerequisites

| Provider | Requirements and transport |
|---|---|
| Local model | An already-running OpenAI-compatible loopback server with `/models` and `/chat/completions`. Default: `http://127.0.0.1:11434/v1`. Select an installed model; the app does not download or start models. Non-loopback endpoints, embedded credentials and redirects are rejected. |
| Claude subscription | An installed, signed-in Claude CLI. The app locates the executable or accepts its explicit path. Requests use an isolated temporary working/configuration directory, copying the existing subscription credential rather than user plugins, history or project instructions. Tools, MCP, hooks and session persistence are disabled for these requests. |
| ChatGPT subscription | An existing Codex ChatGPT login (`CODEX_HOME/auth.json`, otherwise the user's `.codex/auth.json`). Model discovery and requests use HTTPS to the ChatGPT Codex backend directly, not an agent CLI run. API-key logins are rejected; expired credentials must be refreshed in Codex. |

Subscription access, account limits, CLI switches and backend compatibility may
change. This beta is not an official integration or a promise that any particular
model is available. Local reasoning controls are offered only for recognized
GPT-OSS model IDs; other model/server combinations may reject the request format.

## What each mode reads and sends

**Find by name** converts bounded recent user messages and a prior search plan
into a validated plan. Absolute paths in that planning context are redacted; the
app does not send file-result paths or read document bodies for this mode. The
existing filename index supplies real candidates, and the app applies name,
extension, item-kind, folder-scope, date and size filters locally. It examines at
most 2,000 candidates and returns at most 200 results; narrow a limited search.
The filename index must be ready.

**Search contents** investigates on demand: the app searches names, lists candidate
folders and reads selected candidate documents. It is not a permanent full-text
index and cannot prove that the whole disk contains no matching information.
Candidate paths, file metadata, bounded document excerpts, supporting quotations
and conversation context can be sent to the selected provider. **With a
subscription provider, that information leaves the computer.** Do not use this
mode for sensitive files unless that provider's handling is acceptable to you.
Using a loopback endpoint does not control the local server's own logging or
network behavior.

Each contents request has a three-minute limit, at most 16 model actions, eight
folder listings, eight document reads, 256 candidates and 48,000 excerpt
characters. Each listing returns at most 128 immediate entries. Each file is
limited to 20 MB, 12 excerpts and 12,000 characters by default. Unsupported,
failed, truncated or limited reads are reported as incomplete coverage, not as
an exhaustive no-match. The model proposes read-only actions over observed IDs;
the app validates actions and exact citation quotations. It does not execute
model-generated shell commands or edit source documents.

Text formats are TXT, Markdown, CSV and LOG. The bundled parser handles PDF,
HWP/HWPX/HML, DOCX, PPTX and XLS/XLSX. OCR is disabled: image-only/scanned documents
may have no readable text. Locators distinguish real pages/slides/sheets from
approximate sections or unpaginated text. See the
[parser packaging and redistribution notes](../tools/DocumentParser/README.md).

## Persistence and context display

AI settings are saved beside the chosen session with an `.ai-search.json`
extension. They include provider/model selection, endpoint/CLI paths, personal
instructions and panel layout, but not subscription credentials or conversation
bodies. Personal instructions are saved as plain text, are limited to 2,000
characters and are sent with requests; do not put secrets in them. Conversation
history is in memory, not restored on app restart. Hiding the panel cancels the
pending request but is not a substitute for **New chat**.

The context meter reports input usage for the last successful request when the
provider supplies usable metadata. A capacity/percentage is shown only when
available; it is not a measured size of a persistent agent session or a guarantee
of the model's remaining capacity.

## Development and packaging

The branch includes `tests/AiSearch.Tests` and AI-specific native UI scenarios.
`build.ps1` includes the backend suite; `-UiTests` additionally uses the existing
private-desktop QA harness. Live subscription scenarios/probe scripts are
explicit opt-ins and can make authenticated network requests and consume account
quota; they are not part of the default backend run. The capability probe script
uses Bun, while `SubscriptionWireProbe.mjs` uses standard Node APIs.

`build.ps1 -Publish` stages the offline document reader and creates a local ZIP;
it is a local packaging command, not a GitHub release command. Packaging needs
the .NET SDK, PowerShell and Node/npm, plus network access to fetch build-time
packages. Keep the entire `parsers/` directory and dependency license notices
beside the executable. A plain .NET build does not stage this directory, so
non-text document reads need it supplied separately. End users of a complete
package do not need Node, Office or Hancom installed.

Keep sessions, search indexes, downloaded dependencies, binaries, logs and local
QA evidence out of commits. Existing ignore rules cover `artifacts/`, `.omo/`,
`qa-workspace/`, `bin/`, `obj/` and `node_modules/`. Use a separate `--session`
path for a trial rather than the stable application's session, and disable that
trial's update, global-hotkey and tray preferences when running side by side.
