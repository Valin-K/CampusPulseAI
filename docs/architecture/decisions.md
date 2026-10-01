# Architecture Decision Records

Decisions already embodied in the Release 0 codebase, recorded so the reasoning survives.

---

## ADR-001 — Database as a separate service per domain

**Status:** Accepted · **Context:** Release 0

Each domain owns a database *service*, not a database *file*. Backends never open SQLite
directly; they call their database service over HTTP through a named `HttpClient`
(`billingDatabase`, `CampusHousingDatabase`, `eventDatabase`, and so on).

**Why:** it keeps each domain's storage genuinely private, makes every cross-domain read
an explicit network call that can be observed and tested, and lets a single student own
their schema without coordinating migrations with four other people.

**Cost:** an extra container and HTTP hop per domain, and startup ordering becomes a real
concern — see ADR-004.

---

## ADR-002 — SQLite as the store

**Status:** Accepted · **Context:** Release 0

Every domain persists to a single SQLite file under `/app/data`, configured through a
`ConnectionStrings__*` environment variable.

**Why:** zero operational overhead for a five-developer student project, no server to
provision, and the file is trivially seeded and reset for demonstrations.

**Cost:** a single file is a single point of failure, and it only survives container
recreation when a named volume is mounted — which today is true for two of five domains.
Migration to a networked database is the natural Release 1+ step if concurrency matters.

---

## ADR-003 — Environment-variable service discovery

**Status:** Accepted · **Context:** Release 0

Peers are resolved from `Services__*` configuration keys, each defaulting to a Compose
service name (for example `Services__AcademicRecordsBackend` →
`http://academicrecords.backend:8080`).

**Why:** it works identically in Compose, in CI and on a developer machine with an
override, and it needs no service mesh, registry or gateway. The approved project context
explicitly rules those out for Release 0.

**Cost:** no health-aware routing, no retries or load balancing for free — each caller
implements its own resilience.

---

## ADR-004 — Application-level startup resilience over orchestrator guarantees

**Status:** Accepted · **Context:** Release 0

Only two databases publish a healthcheck, and only Campus Housing waits on
`service_healthy`. Everything else tolerates a not-yet-ready dependency in application
code — Financial Aid & Billing retries reads 30 times at one-second intervals via
`DatabaseStartupRetryHandler`.

**Why:** it makes services robust regardless of how they are started, including outside
Compose entirely.

**Cost:** the resilience strategy is inconsistent across domains, and a genuinely failed
dependency looks the same as a slow one until the retry budget is exhausted. Extending
healthchecks to all five databases would let `depends_on` carry more of this weight.

---

## ADR-005 — Direct Gemini calls from each backend

**Status:** Accepted · **Context:** Release 0

AI features call the Gemini `generateContent` API directly from the owning backend, with
`GEMINI_API_KEY` injected as `${GEMINI_API_KEY:-}`. The `mcpserver`, `multiagentserver`
and `ragserver` containers exist in the topology but are unimplemented scaffolds that
nothing references.

**Why:** it let each student ship a working AI feature independently, without waiting on
shared AI infrastructure.

**Cost:** prompt construction, timeout handling and failure semantics are duplicated five
times and have already diverged — one service sends a student's full name to the model
while another's requirements forbid exactly that. Consolidating behind the AI services is
the obvious Release 1 candidate.

---

## ADR-006 — Shared Blazor component library

**Status:** Partially adopted · **Context:** Release 0

`shared/frontend/CampusPulse.Shared.Components` supplies `CampusShell`, `CampusNavBar`,
`CampusMetricCard` and `CampusSectionTabs`, with a shared theme and the `shared-ui`
landing shell on port 8080.

**Why:** consistent navigation, terminology and visual theme across five independently
developed frontends.

**Status note:** only the student-4 and student-5 frontends take the project reference
today. Students 1, 2 and 3 reproduce the styling locally, so consistency is currently
convention rather than something the build enforces.

---

## ADR-007 — Human-gated agentic loop

**Status:** Accepted · **Context:** Release 0

`scripts/agentic-loop/agentic_loop.py` runs Plan → Act → Observe → Adapt → Verify, with
deterministic checks between the AI stages and an independent review model. It never
writes to the repository on its own: applying a change requires `--apply`, an
`--approved-patch` file and human `--approval-text`, and patches touching binaries or
paths outside the repository are rejected.

**Why:** AI proposals are treated as evidence to be checked, not instructions to be
followed. Every run leaves a JSON audit trail in
`docs/release-0/agentic-loop-evidence/`.

**Cost:** no unattended remediation — a human is in the loop for every change.

---

## ADR-008 — Shared AI service as the LLM gateway

**Status:** Accepted · **Context:** Release 1

The shared AI service (`CampusPulse.Shared.Ai.Gateway`) is the common gateway for
microservices that need LLM generation or embeddings. Any microservice can access it
through the shared AI client and the `Services:AiGateway` endpoint configuration. The
gateway owns provider credentials, model configuration, and provider HTTP calls.

**Why:** a single integration boundary makes AI access available across domains while
centralizing provider configuration, credentials, and transport behavior.

**Cost:** the gateway becomes a shared runtime dependency; callers need to handle its
availability and configure its endpoint.
