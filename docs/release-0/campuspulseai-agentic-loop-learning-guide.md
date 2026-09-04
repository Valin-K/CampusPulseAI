# CampusPulseAI Shared Agentic Loop Learning Guide

This guide explains the shared agentic loop for the whole CampusPulseAI solution.

## The simple idea

The loop follows the DevOps feedback cycle:

```
PLAN -> ACT -> OBSERVE -> ADAPT -> VERIFY
```

1. **PLAN** — an AI agent turns the high-level goal into a small, testable plan.
2. **ACT** — an AI agent proposes changes based on the plan and approved context.
3. **OBSERVE** — deterministic checks run (build, test, Docker Compose, HTTP smoke
   tests) and an AI agent reviews the proposal against the evidence.
4. **ADAPT** — a human decides whether to apply a patch.
5. **VERIFY** — the same deterministic checks run again after an approved patch.

## What makes this loop "shared"

The previous loop only validated `student-2`. This loop validates the entire
solution:

- `student-1` Academic Records
- `student-2` Course Management
- `student-3` Campus Housing
- `student-4` Financial Aid and Billing
- `student-5` Event Scheduling
- `ai-services/` (MCP server, multi-agent server, RAG server)
- `shared/frontend` shared Blazor components
- Root-level `docker-compose.yml` and `Directory.Packages.props`

## AI providers and models

The script is provider-agnostic:

- **Primary provider**: OpenRouter (default), configured with `OPENROUTER_API_KEY`.
  The default model is `qwen/qwen3.8-27b`.
- **Fallback provider**: Google Gemini, configured with `GEMINI_API_KEY`. The
  default model is `gemini-3.5-flash`.

If OpenRouter returns a key/limit error (401, 403, 429), the loop automatically
falls back to Gemini when a Gemini key is available. You can also force Gemini
by setting `AGENTIC_PROVIDER=gemini` in `.env`.

## Deterministic checks

AI outputs are opinions. The following checks produce objective evidence:

| Check | Purpose |
|---|---|
| `git diff --check` | No whitespace errors in pending changes. |
| `dotnet restore CampusPulseAI.slnx` | All NuGet packages resolve. |
| `dotnet build CampusPulseAI.slnx` | Entire solution compiles. |
| `dotnet test CampusPulseAI.slnx` | All student test projects pass. |
| `docker compose config --quiet` | Docker Compose file is valid. |
| Per-student HTTP smoke tests | Best-effort live service checks (skipped if services are not running). |

## Human approval

The loop never applies changes automatically. To apply a patch, run:

```powershell
python scripts\agentic-loop\agentic_loop.py `
    --context docs\release-0\agentic-loop-context\campuspulseai-context.txt `
    --approved-patch C:\path\to\approved-change.patch `
    --approval-text "Approved after reviewing the plan, proposal, and tests." `
    --apply
```

The script validates that the patch only targets files inside the repository,
applies it with `git apply`, and reruns the deterministic checks.

## Evidence

Each run writes one JSON file to:

```text
docs\release-0\agentic-loop-evidence\campuspulseai-<timestamp>.json
```

The evidence records:

- requested and actual AI provider / model for each stage;
- every prompt sent to the AI agents;
- raw AI outputs;
- the approved project context;
- all deterministic check results;
- human approval text and applied patch targets;
- branch and commit identifiers;
- any error that caused the run to fail.

API keys are never recorded in evidence files.

## Approved context

The context file at:

```text
docs\release-0\agentic-loop-context\campuspulseai-context.txt
```

keeps the agents grounded in facts. It describes the five student services, the
shared infrastructure, the deterministic checks that already pass, and the rules
that prevent the agents from inventing new dependencies or replacing existing
files with invented content.

## Running the loop

From the repository root:

```powershell
pip install -r scripts\agentic-loop\requirements.txt
python scripts\agentic-loop\agentic_loop.py `
    --context docs\release-0\agentic-loop-context\campuspulseai-context.txt
```

## Release 0 limitation

Live HTTP smoke tests are skipped when the backend services are not running
locally. The deterministic build, test, and Docker Compose checks still prove
that the solution is structurally sound. For a full end-to-end validation, run
the services with `docker compose up` before the loop.
