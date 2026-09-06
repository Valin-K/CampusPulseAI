"""
CampusPulseAI Shared Agentic Loop
=================================

A Python implementation of the Plan -> Act -> Observe -> Adapt -> Verify
workflow for the entire CampusPulseAI solution.

The loop validates all five student microservices plus the shared Docker
Compose infrastructure. It uses OpenRouter (OpenAI-compatible API) for the
AI stages, defaulting to a Qwen 2.5 model.

Configuration is read from a `.env` file in the repository root.

Usage:
    # Install dependencies once
    pip install -r scripts/agentic-loop/requirements.txt

    # Review-only run
    python scripts/agentic-loop/agentic_loop.py

    # Run with a specific focus
    python scripts/agentic-loop/agentic_loop.py --focus architecture

    # Apply an approved patch after human review
    python scripts/agentic-loop/agentic_loop.py `
        --approved-patch C:\\path\\change.patch `
        --approval-text "Approved after reviewing the plan and tests." `
        --apply
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import requests
from dotenv import load_dotenv

# ---------------------------------------------------------------------------
# Paths and environment
# ---------------------------------------------------------------------------
REPO_ROOT = Path(__file__).resolve().parent.parent.parent
ENV_PATH = REPO_ROOT / ".env"
load_dotenv(dotenv_path=ENV_PATH, override=True)

EVIDENCE_DIR = REPO_ROOT / "docs" / "release-0" / "agentic-loop-evidence"

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
OPENROUTER_API_KEY = os.getenv("OPENROUTER_API_KEY")
OPENROUTER_BASE_URL = os.getenv(
    "OPENROUTER_BASE_URL", "https://openrouter.ai/api/v1"
).rstrip("/")
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
AGENTIC_PROVIDER = os.getenv("AGENTIC_PROVIDER", "openrouter").lower()
DEFAULT_MODEL = os.getenv("AGENTIC_MODEL", "qwen/qwen3.8-27b")
PLAN_MODEL = os.getenv("AGENTIC_PLAN_MODEL", DEFAULT_MODEL)
ACT_MODEL = os.getenv("AGENTIC_ACT_MODEL", DEFAULT_MODEL)
REVIEW_MODEL = os.getenv("AGENTIC_REVIEW_MODEL", DEFAULT_MODEL)

# Gemini model used when OpenRouter is unavailable or when AGENTIC_PROVIDER=gemini.
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-3.5-flash")

STUDENTS = [
    {
        "id": 1,
        "name": "AcademicRecords",
        "display": "Academic Records",
        "folder": "student-1",
        "local_port": 5020,
        "compose_port": 5020,
    },
    {
        "id": 2,
        "name": "CourseManagement",
        "display": "Course Management",
        "folder": "student-2",
        "local_port": 5118,
        "compose_port": None,
    },
    {
        "id": 3,
        "name": "CampusHousing",
        "display": "Campus Housing",
        "folder": "student-3",
        "local_port": 5180,
        "compose_port": 5083,
    },
    {
        "id": 4,
        "name": "FinancialAidBilling",
        "display": "Financial Aid and Billing",
        "folder": "student-4",
        "local_port": 5164,
        "compose_port": 8089,
    },
    {
        "id": 5,
        "name": "EventScheduling",
        "display": "Event Scheduling",
        "folder": "student-5",
        "local_port": 5021,
        "compose_port": 8090,
    },
]

DEFAULT_CONTEXT = """CampusPulseAI is a .NET 10 campus-services solution.

Student services and ownership:
- student-1 / AcademicRecords: student records, transcripts, academic history.
- student-2 / CourseManagement: course catalogue, enrolment gates, syllabus handling.
- student-3 / CampusHousing: housing placements, maintenance requests.
- student-4 / FinancialAidBilling: invoices, payments, billing integrations.
- student-5 / EventScheduling: campus events, scheduling, room bookings.

Shared infrastructure:
- docker-compose.yml defines all backend, database, frontend, and AI services.
- Directory.Packages.props centrally manages NuGet package versions.
- shared/frontend contains shared Blazor components and UI assets.
- ai-services/ contains the MCP server, multi-agent server, and RAG server.

Each student service contains a backend API, a Blazor WebAssembly frontend, a
database project, and an NUnit/XUnit test project. Services communicate over
HTTP inside the Docker Compose network.
"""


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def run_command(
    name: str,
    command: list[str],
    cwd: Path = REPO_ROOT,
    timeout: int = 600,
) -> dict[str, Any]:
    """Run a shell command and return a standard evidence check record."""
    print(f"  {name}")
    started = time.perf_counter()
    try:
        result = subprocess.run(
            command,
            cwd=cwd,
            capture_output=True,
            text=True,
            timeout=timeout,
            encoding="utf-8",
            errors="replace",
        )
        output = result.stdout
        if result.stderr:
            output += "\n" + result.stderr
        exit_code = result.returncode
    except subprocess.TimeoutExpired as exc:
        output = f"Timed out after {timeout}s.\n{exc.stdout or ''}\n{exc.stderr or ''}"
        exit_code = 124
    except FileNotFoundError as exc:
        output = f"Command not found: {exc}"
        exit_code = 127
    except Exception as exc:  # pragma: no cover - defensive
        output = f"Unexpected error: {exc}"
        exit_code = 1

    duration = round(time.perf_counter() - started, 2)
    return {
        "name": name,
        "passed": exit_code == 0,
        "skipped": False,
        "exitCode": exit_code,
        "durationSeconds": duration,
        "output": output.strip(),
    }


def run_http_check(name: str, url: str, timeout: int = 5) -> dict[str, Any]:
    """
    Try a live HTTP check.

    - Any HTTP response (including 404) means the service is reachable and is
      treated as a pass for a smoke test.
    - A connection refusal means the service is not running and is recorded as
      skipped, not a hard failure.
    - A server error (5xx) is a genuine failure.
    """
    print(f"  {name} ({url})")
    started = time.perf_counter()
    try:
        response = requests.get(url, timeout=timeout)
        duration = round(time.perf_counter() - started, 2)
        passed = response.status_code < 500
        return {
            "name": name,
            "passed": passed,
            "skipped": False,
            "exitCode": 0 if passed else 1,
            "durationSeconds": duration,
            "output": f"HTTP {response.status_code}\n{response.text[:500]}",
        }
    except requests.exceptions.ConnectionError as exc:
        duration = round(time.perf_counter() - started, 2)
        return {
            "name": name,
            "passed": False,
            "skipped": True,
            "exitCode": 0,
            "durationSeconds": duration,
            "output": f"Service not reachable: {exc}",
        }
    except Exception as exc:
        duration = round(time.perf_counter() - started, 2)
        return {
            "name": name,
            "passed": False,
            "skipped": False,
            "exitCode": 1,
            "durationSeconds": duration,
            "output": f"HTTP check error: {exc}",
        }


def run_all_deterministic_checks() -> list[dict[str, Any]]:
    """Run the full shared validation suite."""
    checks: list[dict[str, Any]] = []

    # Source hygiene
    checks.append(run_command(
        "Git diff validation",
        ["git", "diff", "--check"],
    ))

    # Solution-level build and test
    checks.append(run_command(
        "dotnet restore solution",
        ["dotnet", "restore", "CampusPulseAI.slnx", "--nologo"],
    ))
    checks.append(run_command(
        "dotnet build solution",
        ["dotnet", "build", "CampusPulseAI.slnx", "--no-restore", "--nologo"],
    ))
    checks.append(run_command(
        "dotnet test solution",
        ["dotnet", "test", "CampusPulseAI.slnx", "--no-build", "--nologo"],
    ))

    # Docker Compose validation
    checks.append(run_command(
        "Docker Compose config validation",
        ["docker", "compose", "config", "--quiet"],
    ))

    # Per-student live HTTP smoke tests (best effort; services may not be running)
    for student in STUDENTS:
        ports = [p for p in (student["local_port"], student["compose_port"]) if p]
        checked = False
        for port in ports:
            url = f"http://localhost:{port}/"
            check = run_http_check(
                f"{student['display']} backend smoke test", url
            )
            if not check["skipped"]:
                checks.append(check)
                checked = True
                break
            # If skipped on first URL, try the next one
        if not checked:
            # Record one skipped check so the student appears in evidence
            checks.append(run_http_check(
                f"{student['display']} backend smoke test",
                f"http://localhost:{ports[0]}/",
            ))

    return checks


def check_summary(checks: list[dict[str, Any]]) -> str:
    lines = []
    for check in checks:
        if check["skipped"]:
            status = "SKIPPED"
        elif check["passed"]:
            status = "PASS"
        else:
            status = "FAIL"
        lines.append(f"{check['name']}: {status}")
    return "\n".join(lines)


def failed_checks(checks: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [c for c in checks if not c["passed"] and not c["skipped"]]


# ---------------------------------------------------------------------------
# OpenRouter / OpenAI-compatible client
# ---------------------------------------------------------------------------
def call_openrouter(
    model: str,
    system_prompt: str,
    user_prompt: str,
    max_tokens: int = 1200,
    temperature: float = 0.2,
) -> tuple[str | None, str | None]:
    """Call an OpenAI-compatible chat endpoint (OpenRouter by default)."""
    if not OPENROUTER_API_KEY:
        return None, "OPENROUTER_API_KEY is not set."

    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
    }
    payload = {
        "model": model,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt},
        ],
        "max_tokens": max_tokens,
        "temperature": temperature,
    }

    try:
        response = requests.post(
            f"{OPENROUTER_BASE_URL}/chat/completions",
            headers=headers,
            json=payload,
            timeout=180,
        )
        response.raise_for_status()
        data = response.json()
        content = data["choices"][0]["message"]["content"]
        if content and content.strip():
            return content.strip(), None
        return None, f"{model} returned an empty response."
    except requests.exceptions.HTTPError as exc:
        return None, f"{model} HTTP error: {exc.response.status_code} - {exc.response.text}"
    except Exception as exc:
        return None, f"{model} request failed: {exc}"


def call_gemini(
    model: str,
    system_prompt: str,
    user_prompt: str,
    max_tokens: int = 1200,
    temperature: float = 0.2,
    max_retries: int = 3,
) -> tuple[str | None, str | None]:
    """Call the Google Gemini generateContent endpoint with 503 retries."""
    if not GEMINI_API_KEY:
        return None, "GEMINI_API_KEY is not set."

    url = (
        "https://generativelanguage.googleapis.com/v1beta/models/"
        f"{model}:generateContent"
    )
    payload = {
        "systemInstruction": {"parts": [{"text": system_prompt}]},
        "contents": [
            {
                "role": "user",
                "parts": [{"text": user_prompt}],
            }
        ],
        "generationConfig": {
            "temperature": temperature,
            "maxOutputTokens": max_tokens,
        },
    }

    last_error = None
    for attempt in range(1, max_retries + 1):
        try:
            response = requests.post(
                url,
                headers={"x-goog-api-key": GEMINI_API_KEY},
                json=payload,
                timeout=300,
            )
            if response.status_code == 503 and attempt < max_retries:
                last_error = f"{model} HTTP error: 503 - {response.text}"
                wait = 2 ** attempt
                print(f"    Gemini {model} returned 503 (attempt {attempt}); retrying in {wait}s...")
                time.sleep(wait)
                continue
            response.raise_for_status()
            data = response.json()
            parts = data["candidates"][0]["content"]["parts"]
            text = "".join(part.get("text", "") for part in parts)
            if text and text.strip():
                return text.strip(), None
            return None, f"{model} returned an empty response."
        except requests.exceptions.HTTPError as exc:
            return None, f"{model} HTTP error: {exc.response.status_code} - {exc.response.text}"
        except Exception as exc:
            return None, f"{model} request failed: {exc}"

    return None, last_error or f"{model} failed after {max_retries} retries."


def is_key_error(error: str | None) -> bool:
    """Detect auth/key/limit failures that warrant a provider fallback."""
    if not error:
        return False
    lowered = error.lower()
    return any(
        phrase in lowered
        for phrase in [
            "401",
            "403",
            "429",
            "not set",
            "key limit exceeded",
            "missing authentication",
            "user not found",
        ]
    )


def call_ai(
    provider: str,
    model: str,
    system_prompt: str,
    user_prompt: str,
    max_tokens: int = 1200,
    temperature: float = 0.2,
) -> tuple[str | None, str | None, dict[str, Any]]:
    """
    Call the configured AI provider/model.

    Returns a tuple of (content, error, metadata). A stage can be routed to
    Gemini explicitly by setting its model ID to a Gemini model name
    (e.g. AGENTIC_REVIEW_MODEL=gemini-3.5-flash). When the primary provider is
    OpenRouter and it fails for key/limit reasons, Gemini is tried automatically
    if GEMINI_API_KEY is available.
    """
    metadata: dict[str, Any] = {"primaryProvider": provider, "primaryModel": model}

    # Detect explicit Gemini model selection per stage.
    is_gemini_model = model.startswith("gemini-")

    if provider == "gemini" or is_gemini_model:
        gemini_model = GEMINI_MODEL if not is_gemini_model else model
        content, error = call_gemini(
            gemini_model, system_prompt, user_prompt, max_tokens, temperature
        )
        metadata["usedProvider"] = "gemini"
        metadata["usedModel"] = gemini_model
        return content, error, metadata

    # Default / OpenRouter path
    content, error = call_openrouter(
        model, system_prompt, user_prompt, max_tokens, temperature
    )
    if content or not is_key_error(error):
        metadata["usedProvider"] = "openrouter"
        metadata["usedModel"] = model
        return content, error, metadata

    # OpenRouter key/limit failure -> try Gemini fallback
    if GEMINI_API_KEY:
        print(
            f"    OpenRouter failed ({error}); trying Gemini fallback ({GEMINI_MODEL})."
        )
        gemini_content, gemini_error = call_gemini(
            GEMINI_MODEL, system_prompt, user_prompt, max_tokens, temperature
        )
        if gemini_content:
            metadata["usedProvider"] = "gemini (fallback)"
            metadata["usedModel"] = GEMINI_MODEL
            return gemini_content, None, metadata
        print(f"    Gemini fallback also failed: {gemini_error}")
        metadata["usedProvider"] = "openrouter"
        metadata["usedModel"] = model
        metadata["fallbackError"] = gemini_error
        return None, error, metadata

    metadata["usedProvider"] = "openrouter"
    metadata["usedModel"] = model
    return content, error, metadata


# ---------------------------------------------------------------------------
# Prompts
# ---------------------------------------------------------------------------
def plan_prompt(goal: str, focus: str, context: str) -> str:
    return f"""You are the planning agent for CampusPulseAI, a .NET 10 campus-services solution with five student microservices.

Student services:
1. Academic Records (student-1): student records, transcripts, academic history.
2. Course Management (student-2): course catalogue, enrolment gates, syllabus handling.
3. Campus Housing (student-3): housing placements, maintenance requests.
4. Financial Aid and Billing (student-4): invoices, payments, billing integrations.
5. Event Scheduling (student-5): campus events, scheduling, room bookings.

Goal: {goal}
Focus: {focus}

Approved project context:
{context}

Create a small, shared validation and implementation plan that a beginner can follow.
Use only the approved context above. Do not invent business rules, dependencies, or
endpoints that are not explicitly covered by the context.

Reply using exactly:
User journey:
Business rule:
Implementation steps:
Acceptance criteria:
Risks:
"""


def act_prompt(goal: str, plan_output: str, context: str) -> str:
    return f"""You are the implementation agent for CampusPulseAI.

Goal:
{goal}

Plan:
{plan_output}

Approved project context:
{context}

The approved context overrides any conflicting statement in the plan.
Review the existing implementation across the five student services and propose only
a remaining change that is necessary. If the solution already meets the goal, say that
no code adaptation is needed. Do not apply files and do not claim tests ran.

Reply using exactly:
Plan interpretation:
File-by-file changes:
Tests:
Risks or questions:
"""


def observe_prompt(
    goal: str,
    plan_output: str,
    act_output: str,
    context: str,
    checks_summary: str,
) -> str:
    return f"""You are the independent reviewer for CampusPulseAI.

Goal:
{goal}

Plan:
{plan_output}

Implementation proposal:
{act_output}

Approved project context:
{context}

Objective checks:
{checks_summary}

Identify what should be kept, corrected, or rejected.
Reject any suggestion that adds a business rule, dependency, or endpoint not present
in the approved context. Do not claim that you inspected source code.

Reply using exactly:
Keep:
Correct:
Reject:
Recommended adaptation:
Verification:
"""


# ---------------------------------------------------------------------------
# Patch handling
# ---------------------------------------------------------------------------
def validate_patch_targets(patch_path: Path) -> list[str]:
    """Ensure an approved patch only touches files inside the repository."""
    text = patch_path.read_text(encoding="utf-8")
    if "GIT binary patch" in text:
        raise ValueError("Binary patches are not allowed.")

    targets = sorted(
        {m.group(1).strip() for m in re.finditer(r"(?m)^\+\+\+ b/(.+)$", text)}
    )
    if not targets:
        raise ValueError("The approved patch does not contain a changed file.")

    for target in targets:
        resolved = (REPO_ROOT / target).resolve()
        if REPO_ROOT not in resolved.parents and resolved != REPO_ROOT:
            raise ValueError(
                f"Approved patch target is outside the repository: {target}"
            )
    return targets


def apply_patch(patch_path: Path) -> None:
    run_command(
        "git apply --check",
        ["git", "apply", "--check", "--whitespace=error-all", str(patch_path)],
    )
    check = run_command(
        "git apply",
        ["git", "apply", "--whitespace=error-all", str(patch_path)],
    )
    if not check["passed"]:
        raise RuntimeError(f"Patch could not be applied:\n{check['output']}")


# ---------------------------------------------------------------------------
# Main workflow
# ---------------------------------------------------------------------------
def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="CampusPulseAI shared agentic loop",
    )
    parser.add_argument(
        "--focus",
        choices=["implementation", "database", "architecture", "devops"],
        default="implementation",
        help="Area of focus for the loop.",
    )
    parser.add_argument(
        "--goal",
        default="Validate and improve the shared CampusPulseAI campus services solution.",
        help="High-level goal for the agentic loop.",
    )
    parser.add_argument(
        "--context",
        type=Path,
        help="Path to an approved project-context file.",
    )
    parser.add_argument(
        "--approved-patch",
        type=Path,
        dest="approved_patch",
        help="Path to a patch file that has been approved for application.",
    )
    parser.add_argument(
        "--approval-text",
        dest="approval_text",
        default="",
        help="Human approval text required when applying a patch.",
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Apply the approved patch after the Observe stage.",
    )
    parser.add_argument(
        "--evidence-dir",
        type=Path,
        dest="evidence_dir",
        default=EVIDENCE_DIR,
        help="Directory where evidence JSON files are written.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    if not OPENROUTER_API_KEY:
        print(
            "ERROR: OPENROUTER_API_KEY is not set. "
            "Create a .env file in the repository root and add your key.",
            file=sys.stderr,
        )
        return 1

    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    evidence_path = args.evidence_dir / f"campuspulseai-{run_id}.json"
    args.evidence_dir.mkdir(parents=True, exist_ok=True)

    # Resolve context
    if args.context:
        context = args.context.read_text(encoding="utf-8")
        context_path = str(args.context)
    else:
        context = DEFAULT_CONTEXT
        context_path = "(default in-script context)"

    # Working state
    plan_output = None
    act_output = None
    observe_output = None
    plan_error = None
    act_error = None
    observe_error = None
    plan_meta: dict[str, Any] | None = None
    act_meta: dict[str, Any] | None = None
    observe_meta: dict[str, Any] | None = None
    run_error = None
    checks_before: list[dict[str, Any]] = []
    checks_after: list[dict[str, Any]] = []
    commit_before = None
    commit_after = None
    patch_applied = False
    patch_targets: list[str] = []

    try:
        commit_before = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()

        # ------------------------------------------------------------------
        # PLAN
        # ------------------------------------------------------------------
        print("=" * 60)
        print(f"PLAN - {PLAN_MODEL}")
        print("=" * 60)
        plan_prompt_text = plan_prompt(args.goal, args.focus, context)
        plan_output, plan_error, plan_meta = call_ai(
            AGENTIC_PROVIDER,
            PLAN_MODEL,
            "You are a concise planning assistant for a .NET campus-services solution.",
            plan_prompt_text,
            max_tokens=4000,
            temperature=0.2,
        )
        if plan_error:
            raise RuntimeError(plan_error)
        print(f"    (used {plan_meta['usedProvider']} / {plan_meta['usedModel']})")
        print(plan_output)

        # ------------------------------------------------------------------
        # ACT
        # ------------------------------------------------------------------
        print("\n" + "=" * 60)
        print(f"ACT - {ACT_MODEL}")
        print("=" * 60)
        act_prompt_text = act_prompt(args.goal, plan_output, context)
        act_output, act_error, act_meta = call_ai(
            AGENTIC_PROVIDER,
            ACT_MODEL,
            "You are a cautious implementation assistant for a .NET campus-services solution.",
            act_prompt_text,
            max_tokens=4000,
            temperature=0.1,
        )
        if act_error:
            raise RuntimeError(act_error)
        print(f"    (used {act_meta['usedProvider']} / {act_meta['usedModel']})")
        print(act_output)

        # ------------------------------------------------------------------
        # OBSERVE - deterministic checks
        # ------------------------------------------------------------------
        print("\n" + "=" * 60)
        print("OBSERVE - deterministic checks")
        print("=" * 60)
        checks_before = run_all_deterministic_checks()
        for check in checks_before:
            status = (
                "SKIP" if check["skipped"]
                else "PASS" if check["passed"]
                else "FAIL"
            )
            print(f"    [{status}] {check['name']} ({check['durationSeconds']}s)")

        # ------------------------------------------------------------------
        # OBSERVE - AI review
        # ------------------------------------------------------------------
        print("\n" + "=" * 60)
        print(f"OBSERVE - {REVIEW_MODEL} review")
        print("=" * 60)
        observe_prompt_text = observe_prompt(
            args.goal,
            plan_output,
            act_output,
            context,
            check_summary(checks_before),
        )
        observe_output, observe_error, observe_meta = call_ai(
            AGENTIC_PROVIDER,
            REVIEW_MODEL,
            "You are an independent review assistant for a .NET campus-services solution.",
            observe_prompt_text,
            max_tokens=4000,
            temperature=0.2,
        )
        if observe_error:
            raise RuntimeError(observe_error)
        print(f"    (used {observe_meta['usedProvider']} / {observe_meta['usedModel']})")
        print(observe_output)

        # ------------------------------------------------------------------
        # ADAPT - human approval
        # ------------------------------------------------------------------
        print("\n" + "=" * 60)
        print("ADAPT - human approval")
        print("=" * 60)
        if args.apply:
            if not args.approval_text:
                raise ValueError("Provide --approval-text when applying a patch.")
            if not args.approved_patch:
                raise ValueError("Provide --approved-patch with --apply.")
            patch_path = args.approved_patch.resolve()
            patch_targets = validate_patch_targets(patch_path)
            print(f"Applying approved patch: {patch_path}")
            print(f"Targets: {', '.join(patch_targets)}")
            apply_patch(patch_path)
            patch_applied = True

            # --------------------------------------------------------------
            # VERIFY - repeat deterministic checks
            # --------------------------------------------------------------
            print("\n" + "=" * 60)
            print("VERIFY - repeat deterministic checks")
            print("=" * 60)
            checks_after = run_all_deterministic_checks()
            for check in checks_after:
                status = (
                    "SKIP" if check["skipped"]
                    else "PASS" if check["passed"]
                    else "FAIL"
                )
                print(f"    [{status}] {check['name']} ({check['durationSeconds']}s)")

            if failed_checks(checks_after):
                raise RuntimeError(
                    "The patch was applied, but verification checks failed."
                )
        else:
            print("No patch applied. Human approval is still required.")

        commit_after = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()

    except Exception as exc:  # pragma: no cover - top-level error capture
        run_error = str(exc)
        print(f"\nAgentic loop failed: {run_error}", file=sys.stderr)

    # ------------------------------------------------------------------
    # Evidence
    # ------------------------------------------------------------------
    evidence = {
        "schemaVersion": 4,
        "assessmentEvidence": True,
        "workflow": "Plan -> Act -> Observe -> Adapt -> Verify",
        "runId": run_id,
        "recordedAtUtc": now_iso(),
        "goal": args.goal,
        "focus": args.focus,
        "models": [
            {
                "stage": "Plan",
                "requestedProvider": AGENTIC_PROVIDER,
                "requestedModel": PLAN_MODEL,
                "usedProvider": plan_meta.get("usedProvider") if plan_meta else None,
                "usedModel": plan_meta.get("usedModel") if plan_meta else None,
            },
            {
                "stage": "Act",
                "requestedProvider": AGENTIC_PROVIDER,
                "requestedModel": ACT_MODEL,
                "usedProvider": act_meta.get("usedProvider") if act_meta else None,
                "usedModel": act_meta.get("usedModel") if act_meta else None,
            },
            {
                "stage": "Observe",
                "requestedProvider": AGENTIC_PROVIDER,
                "requestedModel": REVIEW_MODEL,
                "usedProvider": observe_meta.get("usedProvider") if observe_meta else None,
                "usedModel": observe_meta.get("usedModel") if observe_meta else None,
            },
        ],
        "plan": {
            "prompt": plan_prompt_text if plan_output else None,
            "rawOutput": plan_output,
            "error": plan_error,
        },
        "act": {
            "contextPath": context_path,
            "context": context,
            "prompt": act_prompt_text if act_output else None,
            "rawOutput": act_output,
            "error": act_error,
        },
        "observe": {
            "checks": checks_before,
            "prompt": observe_prompt_text if observe_output else None,
            "rawOutput": observe_output,
            "error": observe_error,
        },
        "adapt": {
            "humanApproval": args.approval_text,
            "patchRequested": args.apply,
            "patchApplied": patch_applied,
            "patchPath": str(args.approved_patch) if args.approved_patch else None,
            "targetFiles": patch_targets,
        },
        "verify": {
            "checks": checks_after,
        },
        "privacy": {
            "sourceCodeSentToAi": False,
            "projectContextSentToAi": True,
            "apiKeysRecorded": False,
        },
        "git": {
            "branch": subprocess.run(
                ["git", "branch", "--show-current"],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
            ).stdout.strip(),
            "commitBefore": commit_before,
            "commitAfter": commit_after,
        },
        "error": run_error,
    }

    evidence_path.write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    print(f"\nEvidence written to: {evidence_path}")

    if run_error:
        return 1

    if failed_checks(checks_before) and not args.apply:
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
