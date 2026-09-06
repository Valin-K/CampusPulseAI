# CampusPulseAI Shared Agentic Loop Learning Guide

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

## Multi-model setup used in this run

| Stage | Provider | Model |
|---|---|---|
| Plan | OpenRouter | `qwen/qwen3.8-27b` |
| Act | OpenRouter | `qwen/qwen3.8-27b` |
| Observe (review) | Google Gemini | `gemini-3.5-flash` |

Two models are used to satisfy the multi-model workflow requirement:

- **Qwen 3.8 27B** (via OpenRouter) plans the validation and proposes implementation
  changes.
- **Gemini 3.5 Flash** (via the existing CampusPulseAI Gemini API key) independently
  reviews the Qwen proposal against the deterministic evidence.

The model for each stage is controlled by environment variables in `.env`:

```text
AGENTIC_PLAN_MODEL=qwen/qwen3.8-27b
AGENTIC_ACT_MODEL=qwen/qwen3.8-27b
AGENTIC_REVIEW_MODEL=gemini-3.5-flash
```

Setting a stage model to a Gemini model name (prefix `gemini-`) automatically
routes that stage to the Gemini provider, while other stages continue to use
OpenRouter.

## Deterministic checks

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

## Release 0 result

This review-only loop ran on 2026-09-06. All deterministic checks passed:

- `dotnet restore` succeeded.
- `dotnet build` succeeded.
- `dotnet test` succeeded with 172 tests passing across the five student services.
- `docker compose config --quiet` succeeded.
- Live HTTP smoke tests were skipped because the backend services were not running
  locally.

Both the Qwen implementation agent and the Gemini review agent concluded that
**no code adaptation is needed** — the existing implementation already satisfies
the goal.
