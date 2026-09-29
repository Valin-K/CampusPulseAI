# CampusPulse.Shared.Rag — Shared RAG Classes & Per-Service Knowledge Assistants

This folder holds the **shared backend library for Retrieval-Augmented Generation (RAG)**. It is the
"generation-trust" layer that every feature backend uses to answer knowledge questions with
**grounded, cited** answers. This README is the catch-up for anyone joining the work.

> **One-line summary:** a reusable class library does all the RAG plumbing and citation verification
> once; each feature only supplies **its own corpus** and a thin endpoint. Every service now exposes
> `POST /api/ai/knowledge`.

---

## 1. What this is (and isn't)

- **A class library, not a service.** `CampusPulse.Shared.Rag` has no `Main`, no port. It is **compiled
  into** each backend (and into the RAG server) via a `ProjectReference` — there is no "shared classes
  container".
- **It is the trust layer around the model call**, not the retrieval engine. Embedding, vector search,
  and generation are done by the **RAG server** (`ai-services/rag-server`), which already existed.
- **It grounds answers in shareable knowledge only** — policies + non-personal catalogue data. Personal
  records (transcripts, individual invoices, allocations) are **never** put in the shared vector store;
  each feature's existing direct-model path handles personal analysis.

---

## 2. Library structure — `CampusPulse.Shared.Rag/`

| Folder / file | Responsibility |
|---|---|
| `Contracts/RagContracts.cs` | Wire types for the RAG server: `RagDocument`, `RagIngest*`, `RagAnswer*`, `RagSource`, `RagStatus`. |
| `Client/RagServerClient.cs` (`IRagServerClient`) | HTTP transport to the RAG server (`/documents`, `/answer`). |
| `Generation/RagCitationParser.cs` (+ `ParsedRagAnswer`, `CitationRenderer`) | **Core deliverable.** Resolves `[n]` markers to real sources, renumbers them in first-use order, separates cited vs. uncited, and flags hallucinated markers. |
| `Generation/RagPromptBuilder.cs` (+ `RagPromptRequest`, `RagOutputFormat`) | Assembles the model prompt: identity → grounding → **prompt-injection defence** → citation/JSON rule → persona → numbered context → question. |
| `Generation/RagPersonas.cs` | `RagFeature` — pairs each collection with a default persona (all 5 features). |
| `Assistant/RagKnowledgeService.cs` (+ `RagAssistantAnswer`, `RagKnowledgeSource`) | **The orchestrator.** `ingest → ask(persona) → parse citations → shape` into a clean answer. This is what each feature calls. |

The library has **zero NuGet dependencies** (pure BCL), which keeps the parser/prompt-builder trivially
unit-testable with no key or network.

---

## 3. The pipeline & where each step runs

```
 A user question
   │
   ▼
 FEATURE BACKEND  (student-N/backend)                         ← YOUR per-service code
   1. BuildCorpusAsync()  → policy docs + live catalogue data     (feature owns its data)
   2. RagKnowledgeService.AskAsync(feature, question, corpus)     (shared library, compiled in)
        │
        ├─ IngestAsync(corpus) ─────────────┐
        └─ AnswerAsync(question, persona) ──┤
   │                                        ▼
   │                          RAG SERVER  (ai-services/rag-server, on the host :5267)
   │                            embed → vector search → confidence gate (0.55)
   │                            → RagPromptBuilder.Build()   ← shared library, runs HERE
   │                            → Gemini generate → { answer w/ [n], sources }
   │                                        │
   3. RagCitationParser.Parse(...) ◄────────┘   (shared library, back in the backend)
   4. Shape() → RagAssistantAnswer
   │
   ▼
 Response:  { question, status, reply, confidence, sources[{number,label,documentId,score}], embedder }
```

**Two touchpoints of the shared library sit on opposite sides of the model call:**
- **In:** `RagPromptBuilder` — now wired into the **RAG server**, so *every* collection gets the
  grounding + injection-defence prompt (it replaced the server's old inline prompt).
- **Out:** `RagCitationParser` — runs in the **feature backend**, verifying the model's citations.

> Ingest and answer happen on **every request** (the RAG server's vector store is in-memory and cleared
> on restart), so each ask re-ingests the corpus before querying. Keep corpora small.

---

## 4. Per-service configuration

Every backend exposes **`POST /api/ai/knowledge`** with body `{ "question": "..." }`. The only
feature-specific code is the **corpus builder**; everything else is inherited from the library.

| Service (collection) | Corpus = policy docs + live data | Endpoint style |
|---|---|---|
| **student-1** Academic Records (`academicrecords`) | graduation (144 cp), WAM, GPA bands, enrolment **+ course catalogue** | minimal API |
| **student-2** Course Management (`coursemanagement`) | catalogue, prerequisites, enrolment **+ course catalogue** *(metadata only — syllabus PDFs not indexed yet)* | controller |
| **student-3** Campus Housing (`campushousing`) | allocation, maintenance, rooms **+ dorm-room catalogue** | controller |
| **student-4** Financial Aid (`financialaidbilling`) | scholarship policy **+ scholarship catalogue** | minimal API *(added alongside its existing `/api/assistant/rag/scholarships`)* |
| **student-5** Event Scheduling (`eventscheduling`) | booking/eligibility/hire policy **+ rooms & equipment** | controller (reference implementation) |

**Each service was wired with the same four things:**
1. `ProjectReference` to `CampusPulse.Shared.Rag` (host build).
2. Dockerfile `COPY` of the shared csproj before restore (container build).
3. `docker-compose.yml` → `extra_hosts: host.docker.internal:host-gateway` (reach the host RAG server).
4. DI: `IRagServerClient` (typed HttpClient), `RagCitationParser`, `RagKnowledgeService`, + the feature's own knowledge service.

Policy text is taken from each feature's **real** rules (e.g. student-1's 144 credit points comes from
`GraduationEligibilityCalculator`), not invented.

---

## 5. Running & testing locally

The RAG (and MCP) servers run **on the host**, not in Compose. Backends reach them at
`http://host.docker.internal:5267`.

```powershell
# 1. (optional) real answers need a key:  $env:GEMINI_API_KEY = "your-key"
# 2. start the host AI servers (MCP + RAG) and wait for health:
pwsh -File scripts/launch-local-ai-services.ps1
# 3. run a backend (host) or via `docker compose up --build <service>.backend`, then:
curl.exe -s -X POST http://localhost:<port>/api/ai/knowledge -H "Content-Type: application/json" -d "{\"question\":\"How many credit points do I need to graduate?\"}"
```

Without a `GEMINI_API_KEY` the RAG server runs **keyless**: it returns retrieved sources but
`status: generation_unavailable` (no generated answer). The shared library degrades cleanly, and if a
feature's own database is unreachable the corpus falls back to **policy docs only** instead of failing.

---

## 6. Tests & CI

- **`CampusPulse.Shared.Rag.Tests`** — 30 unit tests for the parser, prompt builder, orchestrator, and
  personas. Pure/deterministic: **no key, no network**.
- **`.github/workflows/shared-backend-ci.yml`** runs them on any change under `shared/backend/**`.
- `integration-ci.yml` compiles the whole solution on every PR (catches cross-service breakage).
- Per-feature corpus-builder tests are **not yet added** (the risky trust logic is covered centrally).

---

## 7. Deferred: the universal routing agent

A `multi-agent-server` was prototyped — it classifies a query as **MCP-tool** vs **RAG** and dispatches
accordingly, reusing this library. It is **stashed** (`git stash`, "Unified AI agent server (deferred)")
and not part of the current build. When it lands, the `AGENT-MIGRATION` comments in each feature's
knowledge service mark the exact `ask + parse` block that moves into the agent; corpus-building stays in
each feature (data ownership).

---

## 8. Gotchas

- **In-memory store** → re-ingest per request; a RAG-server restart empties it.
- **PowerShell `curl` is `Invoke-WebRequest`** — use `curl.exe` or `Invoke-RestMethod`.
- **Gemini rate limits** (HTTP 429) surface as a clean 503 from the backend, not a crash — just retry.
- The library targets net10.0 with `TreatWarningsAsErrors`; keep it dependency-free so it stays testable.
