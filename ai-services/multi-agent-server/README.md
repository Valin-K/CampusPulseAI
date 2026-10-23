# Shared Multi-Agent Server

Release 2 server/caller implementation, following ASD Lab 10's roles and evidence
flow. It is a containerised .NET service, separate from the transport-only
Gemini gateway and the Python developer agentic loop.

## Integrated flow

```text
Five Blazor UIs / PowerShell terminal
  -> assigned backend /api/multi-agent/workflows
  -> live read-only application catalogue + optional user evidence
  -> shared Multi-Agent Server (multiagent.server:8080; host port 5270)
  -> optional MCP tool + relevant RAG source retrieval
  -> Planner (objective, ordered steps, evidence requirements)
  -> Worker (draft, exact evidence IDs, limitations; receives planner output)
  -> Reviewer (recommendation, risks, corrections, retest; receives plan + draft)
  -> Awaiting Human Review
       approve -> approved
       reject  -> rejected
       correct -> new Planner -> Worker -> Reviewer round -> Human Review
```

Human approval does not apply database changes or submit applications. Outputs are
read-only advisory drafts. A reviewer agent's recommendation never constitutes
human approval. Human reviewer names are self-declared in this local demonstration,
not authenticated identities. Do not expose these APIs publicly.

| Application | Live baseline evidence |
| --- | --- |
| Academic Records | Course Management course catalogue |
| Course Management | Its course catalogue |
| Campus Housing | Its room catalogue |
| Financial Aid and Billing | Its scholarship catalogue |
| Event Scheduling | Its facility/room catalogue |

The baseline is collected by the assigned backend, never by a model-selected URL.
Additional evidence is labelled unverified. Workflow retrieval and decisions are
scoped to the caller's feature. This scoping is not a substitute for authentication.
Personalised student eligibility, GPA, allocations, invoices, and bookings must not
be inferred from these catalogues.

## Local configuration

Run the complete environment:

```powershell
docker compose up --build -d
```

Compose starts MCP on loopback port 5268, RAG on 5267, and Multi-Agent on 5270.
Backends explicitly enable the caller and use `http://multiagent.server:8080`.
The server uses container DNS for MCP, RAG, and the gateway, and stores SQLite
workflow history in `/app/data/multi-agent.db` on the `multiagent-data` named volume.
Do not use `docker compose down --volumes` unless you intend to delete that history.

Backend callers forward the current bearer token when starting or correcting a
workflow. The server retains it only in the in-memory queue for that execution and
uses a separate MCP HTTP client per job. Tokens are never persisted with workflows,
sent to models or RAG, or included in evidence exports. Correction rounds use the
review request's current token. Protected downstream APIs still enforce token
expiry and scope; expired credentials produce an explicit dependency failure (or
the caller's selected optional-dependency fallback), not an authentication bypass.

Alternatively run just the server on the host (stop its container first):

```powershell
dotnet run --project .\ai-services\multi-agent-server\MultiAgentServer\MultiAgentServer.csproj
```

For a no-launch-profile invocation, explicitly enable local operation:

```powershell
dotnet run --no-launch-profile --project .\ai-services\multi-agent-server\MultiAgentServer\MultiAgentServer.csproj -- --urls http://localhost:5270 --MultiAgent:Enabled true
```

| Setting | Default |
| --- | --- |
| `Services__MultiAgentServer` (backends) | `http://localhost:5270` |
| `Services__AiGateway` (server) | `http://localhost:5269` |
| `Services__McpServer` (server) | `http://localhost:5268` |
| `Services__RagServer` (server) | `http://localhost:5267` |
| `MultiAgent__DatabasePath` (server) | `.artifacts\multi-agent.db` below server content root |
| `MultiAgent__Enabled` | Server false outside development; caller true in development, false otherwise |

The existing gateway is the only process needing `GEMINI_API_KEY`. It can inherit
the Windows user environment; no second API key or model is necessary. Planner,
Worker, and Reviewer have distinct prompts and structured output contracts while
using the gateway's configured generation model. Dependency URLs and Gemini model
configuration remain independently configurable.

Production/cloud is disabled by default. `Deploy-Apps.ps1 -EnableAi` or the cloud
workflow's `enable_ai` input deploys the four AI containers internally and enables
callers. Set `GEMINI_API_KEY` for the gateway. Multi-Agent uses one always-running
replica while enabled (its queue is in-process) and an Azure Files share for history.

## Frontend demonstration

1. Open any assigned application, then **Ask AI**.
2. Expand **Planner / Worker / Reviewer workflow**.
3. Enter a task appropriate to its catalogue and optionally provide additional evidence.
4. Select MCP/RAG and whether unavailable optional services may use fallback.
5. Start the workflow. The UI polls persisted progress; closing/reloading does not
   cancel server execution. Reopen using **Load workflow history**.
6. Inspect all agent outputs, model names, source evidence, and dependency statuses.
   The status appears above the task form. Scroll inside the assistant panel to
   reach the agent outputs and human-review controls.
7. Enter a human reviewer name and approve, correct, or reject. Comments are
   mandatory for correction/rejection. Correction reruns all agents with the feedback.
8. Select **View workflow and coordination audit evidence (JSON)** to load the
   complete export inside the assistant panel, or export it from the terminal.
   The viewer uses the app's authenticated HTTP client, not a new browser tab:
   navigating directly to the protected API does not attach the session's bearer
   token and returns 401. An expired/missing session prompts you to sign in again.
   Evidence is cleared when you select, refresh, or review a workflow so an older
   export is not shown as the current result.

The evidence viewer works with direct local backend URLs and deployed same-origin
`/api/` proxies, using the frontend's configured HTTP client. Cloud deployments
must enable the optional AI profile (`Deploy-Apps.ps1 -EnableAi` or the workflow's
`enable_ai` input); the default cloud baseline intentionally disables workflows.
Keep the server internal and retain API authentication; no public evidence URL or
token in a URL is required.

## Terminal demonstration

The default caller is Financial Aid and Billing at `http://localhost:8089`.
Use `-BackendUrl` for another backend. Start and wait for the human-review draft:

```powershell
.\scripts\invoke-multi-agent.ps1 -Mode Start -Task "Summarise the current scholarship catalogue. Do not infer student eligibility." -Wait
.\scripts\invoke-multi-agent.ps1 -Mode List
```

Copy the returned workflow ID and version. Read the draft before deciding:

```powershell
$id = "<workflow-guid>"
.\scripts\invoke-multi-agent.ps1 -Mode Get -WorkflowId $id
.\scripts\invoke-multi-agent.ps1 -Mode Review -WorkflowId $id -Decision correct -Reviewer "Tutor" -Comments "Separate published criteria from unverified eligibility." -ExpectedVersion 9 -Wait
```

Use the **new returned version** for the next decision; the example version is not
fixed. Approve or reject:

```powershell
.\scripts\invoke-multi-agent.ps1 -Mode Review -WorkflowId $id -Decision approve -Reviewer "Tutor" -Comments "Checked the evidence and limitations." -ExpectedVersion <current-version>
.\scripts\invoke-multi-agent.ps1 -Mode Export -WorkflowId $id
```

For rejection use `-Decision reject` and explain why in `-Comments`.
`-NoMcp -NoRag` explicitly tests application-evidence-only operation.
`-RequireDependencies` makes a requested MCP/RAG failure fail the workflow instead
of falling back. `-EvidenceFile` adds supplied text without including it in a command.

Direct server execution is also supported, but requires supplied evidence:

```powershell
.\scripts\invoke-multi-agent.ps1 -DirectServer -BackendUrl http://localhost:5270 -Feature coursemanagement -Task "Summarise the supplied course description." -AdditionalEvidence "ASD101 covers Advanced Software Development." -NoMcp -NoRag -Wait
```

Use the same direct-server URL/feature switches for subsequent Get/Review/Export.
Prefer the backend caller for demonstrations of actual application integration.

## API contract

All five backends provide:

| Method | Route | Behaviour |
| --- | --- | --- |
| POST | `/api/multi-agent/workflows` | Collect baseline evidence; queue workflow (202) |
| GET | `/api/multi-agent/workflows` | Feature-scoped persisted history |
| GET | `/api/multi-agent/workflows/{id}` | Current snapshot, outputs, evidence, status, version |
| POST | `/api/multi-agent/workflows/{id}/review` | Approve/reject (200), or queue correction (202) |
| GET | `/api/multi-agent/workflows/{id}/evidence` | Snapshot plus ordered coordination audit |

Start body:

```json
{"task":"Summarise available courses","additionalEvidence":null,"useMcp":true,"useRag":true,"allowFallback":true}
```

Review body:

```json
{"decision":"correct","reviewer":"Tutor","comments":"Explain missing evidence.","expectedVersion":9}
```

Direct server routes are `/api/workflows` and `/api/workflows/{id}` with the same
`review`/`evidence` suffixes. Reads and reviews require `?feature=<feature>`.
Direct start uses `WorkflowRequest`: `feature`, `task`, `evidence` items containing
`id`, `source`, `content`, `collectedAtUtc`, and the three dependency flags.

Limits: task 4000 characters; 1-20 initial evidence items totalling at most 120000
characters; backend additional evidence 20000 characters; baseline catalogue 100000
characters; reviewer name 100 characters; review comments 4000 characters.
MCP/RAG-generated evidence IDs are reserved. Structured worker/reviewer outputs
must reference actual evidence IDs, not invented sources.

Disabled API: 403. Invalid input: 400. Unknown/cross-feature workflow: 404.
Stale or non-reviewable decision: 409. Unavailable live baseline: 502 (no workflow).
Unreachable server: 503. Queue-full: 503 with persisted failed workflow.
An accepted workflow's later agent failure is visible as `status: failed` and an
explicit error when retrieving its snapshot; acceptance is not completion.

## Fallback and failure semantics

- MCP/RAG are optional and explicitly selectable.
- MCP uses read-only `course.list_courses`, `room_status`, or
  `get_feature_capabilities`. Capability evidence describes integration only;
  it is not scholarship/event domain data.
- RAG retrieves existing indexed passages in the feature's collection. Only
  finite scores at least **0.70** are included. This server does not ingest or
  replace collections; use the existing app RAG feature to build its corpus.
- Missing, disabled, malformed, timed-out, or empty-corpus dependencies produce
  visible dependency status and limitations, not fabricated evidence.
- Fallback keeps the real application/supplied baseline and continues all agents.
- Strict mode fails on a requested dependency's unavailability.
- Gateway, model, invalid agent JSON, incomplete contracts, and unknown evidence
  IDs fail the workflow. No mocked success or deterministic substitute is used.
- Jobs are queued in the shared server (100 pending jobs, one at a time); calls
  return immediately. A single gateway request has a 100-second client timeout.
- A server restart marks interrupted queued/running jobs failed, retaining audit
  evidence. Finished and awaiting-review workflows remain available.
- SQLite transactions atomically persist each workflow revision and its audit
  event. Optimistic versions prevent concurrent/stale human decisions.
- Corrections retain source timestamps and earlier rounds. They do not silently
  refresh the original application's baseline; start a new workflow for new data.

## Evidence and tests

SQLite stores complete round history, agent models/times/outputs, source evidence,
dependency outcomes, human decisions, and ordered coordination events. Terminal
Export writes ignored local files under `.artifacts\multi-agent\<workflow-id>`:

- `workflow.json`: complete readable snapshot
- `workflow_history.jsonl`: complete workflow record, including every round
- `coordination_audit.jsonl`: ordered, versioned transition events

Treat exported evidence as potentially sensitive application data. Keep the
database and exports out of source control. Select reviewed demonstration evidence
for the technical report; do not claim test doubles as live-model execution.

```powershell
dotnet test .\ai-services\multi-agent-server\MultiAgentServer.Tests\MultiAgentServer.Tests.csproj
```

Tests cover sequential handoffs; pending human approval; approval, rejection,
correction, stale decisions; persistence/restart; invalid inputs and agent outputs;
explicit dependency fallback and strict failure; exact RAG threshold; direct
HTTP APIs; all five feature callers collecting live evidence and exporting audit.

Assessment mapping: this component supplies the shared containerised server,
Planner -> Worker -> Reviewer -> Human Review through UI and terminal, and workflow
history/coordination evidence. Security scan hooks, CI workflows, Azure deployment,
developer agentic-loop extensions, and the group report/video are separate tasks.
