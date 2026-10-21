# Cloud Deployment — Architecture and Implementation

Covers marking criteria **5 (Cloud Deployment Scripts)**, **6 (Cloud Deployment CI/CD Workflow)** and
**7 (Integrated Cloud Application – CRUD)**. Also produces evidence for criteria 8 and 9, and must not
regress criteria 1 and 3.

Target platform: **Microsoft Azure**, using **Azure Container Apps** and **GitHub Container Registry (GHCR)**.

---

## 1. What we're building

Deploy the integrated application — all 5 students' frontends, backends and databases, plus the shared
portal — to Azure, with **AI-Mode, MCP, RAG and the Multi-Agent Server disabled by default**, per the
brief's cloud baseline. A reusable set of scripts does the deployment, and a GitHub Actions workflow
(`cloud-deployment.yml`) runs those scripts automatically after CI passes on a merge to `master`.

The AI services are containerised and included in local Compose. The required cloud baseline
still does **not deploy** them. The cloud workflow builds and publishes their images alongside
the other images; its `enable_ai` input (or `Deploy-Apps.ps1 -EnableAi`) deploys the bonus AI profile.
`Deploy-Apps.ps1` reads `CAMPUSPULSE-SIGNING-KEY` and, when AI is enabled, `GEMINI-API-KEY`
from Key Vault `kv-campuspulse`. The Azure OIDC identity needs the Key Vault Secrets User role
on that vault. The Gemini key is stored as a Container Apps secret on the gateway only, never
in the images. Local Compose continues to use `CAMPUSPULSE_SIGNING_KEY` and `GEMINI_API_KEY`
from the repository-root `.env` file.

---

## 2. Target architecture

Each of the 5 student projects is really 3 separate pieces (frontend, backend, database), which is already
how `docker-compose.yml` models them. Azure Container Apps mirrors that 1:1 — **one Container App per
compose service**, 16 baseline apps or 20 with AI enabled, inside one Container Apps environment.

```
                         GitHub (Valin-K/CampusPulseAI)
  CI validation ─► student-1..5 CI + integration CI ─► build & push 20 images to GHCR
                                                                      │
                                         az containerapp update --image …:<git-sha> (OIDC login)
                                                                      ▼
 Azure resource group ──────────────────────────────────────────────────────────────────────
  Container Apps environment (all apps minReplicas 0 — see §4)

   Internet ─HTTPS─► campuspulse-portal          (external ingress)
                     academicrecords-web … eventscheduling-web (external ingress, nginx)
                           │  nginx proxies /api/ → backend (same origin, no CORS)
                           ▼
                     academicrecords-api … eventscheduling-api (internal ingress only)
                           ▼
                     academicrecords-db … eventscheduling-db   (internal ingress, maxReplicas 1)
                           ▼
                     Azure Files (SQLite persistence)

   Not deployed in the baseline (bonus only): ai-gateway, mcp-server, rag-server, multi-agent-server
```

### Container App names

Compose service names contain dots (`academicrecords.database`), which Container Apps doesn't allow.
These are the cloud equivalents, used everywhere — Bicep, env vars, nginx `BACKEND_HOST`:

| Compose service | Container App | Ingress |
|---|---|---|
| `academicrecords.database` | `academicrecords-db` | internal |
| `academicrecords.backend` | `academicrecords-api` | internal |
| `academicrecords.frontend` | `academicrecords-web` | external |
| `coursemanagement.database` | `coursemgmt-db` | internal |
| `coursemanagement.backend` | `coursemgmt-api` | internal |
| `coursemanagement.frontend` | `coursemgmt-web` | external |
| `campushousing.database` | `campushousing-db` | internal |
| `campushousing.backend` | `campushousing-api` | internal |
| `campushousing.frontend` | `campushousing-web` | external |
| `financialaidbilling.database` | `finaid-db` | internal |
| `financialaidbilling.backend` | `finaid-api` | internal |
| `financialaidbilling.frontend` | `finaid-web` | external |
| `eventscheduling.database` | `eventscheduling-db` | internal |
| `eventscheduling.backend` | `eventscheduling-api` | internal |
| `eventscheduling.frontend` | `eventscheduling-web` | external |
| `shared-ui` | `campuspulse-portal` | external |
| `ai.gateway` | `ai-gateway` | internal, AI profile only |
| `mcp.server` | `mcp-server` | internal, AI profile only |
| `rag.server` | `rag-server` | internal, AI profile only |
| `multiagent.server` | `multi-agent-server` | internal, AI profile only |

Apps reach each other inside the environment at `http://<app-name>` — Azure's equivalent of Compose's
service-name DNS, confirmed working by hand-testing an internal-ingress app against a public one before
writing any scripted infrastructure.

---

## 3. Key implementation decisions

### 3.1 Frontend → backend: an nginx proxy, not direct browser calls

The 5 frontends are Blazor **WebAssembly** — compiled client-side code the browser runs directly, which
means it's the *browser*, not a server, making the API calls. Previously each frontend's
`wwwroot/appsettings.json` hardcoded `BackendUrl: http://localhost:XXXX`, which only works for local
development: in the cloud, the visitor's browser would try to reach `localhost` on *their own machine*.

Fix: each frontend's nginx config proxies `/api/` to its backend, so the browser only ever calls its own
origin:

```
Browser ──/api/rooms──► frontend's own origin (https://eventscheduling-web…) ──nginx proxy──► backend
```

This also removes any need for cross-origin CORS configuration between frontend and backend, since from
the browser's point of view it's a same-origin call.

`BACKEND_HOST` (an environment variable, rendered into the nginx config via the nginx image's built-in
`envsubst` templating) tells each frontend which backend to proxy to — the Compose service name locally,
the Container App name in the cloud.

**Important detail:** nginx normally resolves a `proxy_pass` hostname once, at container startup, and
refuses to start at all if that host isn't resolvable yet. That's a real problem for Container Apps, where
a backend might not be warmed up yet on a cold start. The fix is to resolve the backend through a variable
(`set $backend_upstream http://${BACKEND_HOST}; proxy_pass $backend_upstream;`) with an explicit
`resolver` directive, which defers DNS lookup to request time — so the frontend container always starts
successfully, and an unreachable backend only fails that one request (502), not the whole app.

Each frontend's `Program.cs` also falls back to its own page origin (`HostEnvironment.BaseAddress`) when
`BackendUrl` isn't configured, via a `wwwroot/appsettings.Production.json` that overrides the local-dev
`BackendUrl` with an empty string for the compiled cloud build.

### 3.2 Image registry: GitHub Container Registry, public images

Images build in GitHub Actions and push to `ghcr.io`, which Azure then pulls from. Images are made
**public** after their first push — this is what keeps registry access simple (no pull credentials needed
on the Azure side) and avoids GHCR's private-tier size/bandwidth limits, which 16 images comfortably
exceed. No source code is exposed this way — only compiled build output (same as what any browser visiting
the deployed frontend can already see).

### 3.3 AI services disabled by default

Each backend reads `Mcp:Enabled`, `Rag:Enabled` and `MultiAgent:Enabled` from configuration. In the cloud
these are explicitly set to `false`, and the UI/API must degrade cleanly (a clear "disabled" response)
rather than erroring when the AI-service URLs aren't configured. The shared Multi-Agent caller
(`CampusPulse.Shared.Ai/MultiAgentEndpoints.cs`) already does this correctly for every student. The
per-feature MCP/RAG orchestrators need the same pattern applied individually — some student backends read
these flags already, others don't yet.

### 3.4 Scale-to-zero by design

The optional AI profile keeps RAG and Multi-Agent at exactly one replica: RAG has an
in-memory index and Multi-Agent has an in-process queue. RAG still requires re-ingestion
after restarts or new revisions. Multi-Agent persists workflow history on its own
`multiagent-data` Azure Files share. This profile has ongoing compute cost while enabled.
The gateway and MCP follow the normal scale-to-zero / warm-up setting.

Every baseline Container App, including the databases, runs with `minReplicas: 0`. SQLite data lives on an Azure
Files share mounted into each database app, so nothing is lost when a container scales down — only the
running compute stops. Databases are additionally capped at `maxReplicas: 1`, since SQLite only tolerates
one writer; this is a correctness requirement, not just a cost one.

The trade-off is a cold-start delay (worst case: web → api → db waking up in sequence) on the first
request after idle. nginx's proxy timeout is set generously (120s) and backends retry their database
connection on startup to absorb this.

### 3.5 Deploy only after CI passes

`cloud-deployment.yml` depends on the 5 student CI workflows and the integration CI workflow via
`needs:`, calling them as **reusable workflows** (`workflow_call`), not `workflow_run`. `workflow_run`
fires once *any* listed workflow finishes — `needs:` on reusable workflows only proceeds once *all* of
them pass, which is the actual "after the required CI/CD validation succeeds" gate the brief asks for.

Images are tagged with the **git SHA**, never `:latest` — Container Apps only creates a new revision when
the image reference actually changes, and SHA tags trace every running revision back to a commit.

### 3.6 Reusable deployment scripts, not inline workflow steps

The deployment logic (Bicep infrastructure, `az containerapp` calls, CRUD/health smoke tests) lives in
standalone scripts under `scripts/deploy/`, not written inline in the YAML. The same scripts run
identically from a developer's machine and from the GitHub Actions workflow — this is what satisfies
Criterion 5 ("reusable deployment scripts") as distinct from Criterion 6 (the workflow that calls them).

---

## 4. Implementation status

| Area | Status |
|---|---|
| CI workflow triggers fixed (`main`/`master`, `workflow_call`) | ✅ Done, all 9 workflows |
| nginx `/api/` proxy + deferred-DNS fix | ✅ Done for Event Scheduling (student-5); validated against a real backend |
| `Program.cs` `BackendUrl` fallback + `appsettings.Production.json` | ✅ Done for Event Scheduling |
| Same frontend/proxy changes for the other 4 students | 🔲 Not started |
| AI kill-switch audit (`Mcp:Enabled`/`Rag:Enabled` per backend) | 🔲 In progress — gap confirmed in Event Scheduling; student-1/student-3 already handle it correctly and are the reference pattern |
| `shared-ui` Dockerfile + configurable portal links | 🔲 Not started |
| Bicep infrastructure + `scripts/deploy/*.ps1` | 🔲 Not started |
| `cloud-deployment.yml` | 🔲 Not started |
| Cloud CRUD smoke tests | 🔲 Not started |

---

## 5. Explicitly out of scope for the required deployment

- AI-Mode, MCP, RAG and Multi-Agent Server running in the cloud (bonus B1–B4 only)
- Cloud endpoint/data security hardening beyond the basics needed to run (bonus B5/B6)
- Anything related to which Azure subscription/account tier to use — that's an account-administration
  detail, not an architecture decision, and is tracked separately from this document.
