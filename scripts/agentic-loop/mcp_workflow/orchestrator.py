from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

from .collector import LAB_REQUIRED_TOOLS, McpCollector
from .pipeline import (
    build_implementation_prompt,
    build_integration_review_prompt,
    render_boundary_analysis,
)


MCP_STAGES = ("OBSERVE", "IMPLEMENTATION", "REVIEW")


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _json_text(value: Any) -> str:
    return json.dumps(value, indent=2, ensure_ascii=False)


def _tool_result_rows(evidence: dict[str, Any]) -> str:
    executions = evidence.get("mcpEvidence", {}).get("toolExecutions", {})
    return "\n".join(
        f"| `{tool_name}` | "
        f"{'PASS' if executions.get(tool_name, {}).get('passed') else 'FAIL / NOT RUN'} |"
        for tool_name in LAB_REQUIRED_TOOLS
    )


def _render_run_report(evidence: dict[str, Any]) -> str:
    stages = "\n".join(
        f"| {stage['name']} | {stage['status'].upper()} | {stage['details']} |"
        for stage in evidence["stages"]
    )
    assessment = evidence.get("implementationAssessment", {}).get("output") or "Not generated."
    review = evidence.get("independentReview", {}).get("output") or "Not generated."
    return f"""# MCP Agentic Workflow Run Report

- Run ID: `{evidence['runId']}`
- Recorded: `{evidence['recordedAtUtc']}`
- Result: **{'PASS' if evidence['passed'] else 'FAIL'}**
- Goal: {evidence['goal']}

## Workflow Stages

| Stage | Status | Evidence |
|---|---|---|
{stages}

## Four-Tool Execution Summary

| Tool | Result |
|---|---|
{_tool_result_rows(evidence)}

## Collected MCP Evidence

```json
{_json_text(evidence.get('mcpEvidence'))}
```

## Implementation Assessment

{assessment}

## Independent Review

{review}

## Human Review

- Decision: **{evidence['humanReview']['decision']}**
- Notes: {evidence['humanReview']['notes'] or 'None recorded.'}

## Reports

{chr(10).join(f"- `{path}`" for path in evidence['reportFiles'].values())}
"""


def _render_integration_report(evidence: dict[str, Any]) -> str:
    assessment = evidence.get("implementationAssessment", {}).get("output") or "Not generated."
    review = evidence.get("independentReview", {}).get("output") or "Not generated."
    passed_tools = sum(
        1
        for tool_name in LAB_REQUIRED_TOOLS
        if evidence.get("mcpEvidence", {}).get("toolExecutions", {})
        .get(tool_name, {})
        .get("passed")
    )
    return f"""# MCP Integration Report

- Workflow status: **{'PASS' if evidence['passed'] else 'INCOMPLETE'}**
- Tool executions passed: **{passed_tools}/4**

## Implementation Assessment

{assessment}

## Independent Review

{review}

## Human Decision

- Decision: **{evidence['humanReview']['decision']}**
- Justification: {evidence['humanReview']['notes'] or 'Pending human review.'}
"""


def _render_tool_review(evidence: dict[str, Any]) -> str:
    review = evidence.get("independentReview", {}).get("output") or "Not generated."
    return f"""# MCP Tool Review

## Independent Evidence-Based Review

{review}

## Human Follow-Up

- Decision: **{evidence['humanReview']['decision']}**
- Notes: {evidence['humanReview']['notes'] or 'Pending human review.'}
- Any accepted correction must be implemented and verified in a subsequent run; this assessment workflow does not mutate source files.
"""


def _write_reports(evidence: dict[str, Any], output_directory: Path) -> None:
    (output_directory / "run-report.md").write_text(
        _render_run_report(evidence), encoding="utf-8"
    )
    (output_directory / "boundary-analysis.md").write_text(
        render_boundary_analysis(evidence.get("mcpEvidence", {})),
        encoding="utf-8",
    )
    (output_directory / "integration-report.md").write_text(
        _render_integration_report(evidence), encoding="utf-8"
    )
    (output_directory / "tool-review.md").write_text(
        _render_tool_review(evidence), encoding="utf-8"
    )


def run_mcp_workflow(
    *,
    server_url: str,
    evidence_dir: Path,
    start_services: Callable[[], list[dict[str, Any]]],
    ai_call: Callable[..., tuple[str | None, str | None, dict[str, Any]]],
    provider: str,
    implementation_model: str,
    review_model: str,
    goal: str = "Validate the CampusPulseAI MCP integration.",
    human_review_decision: str = "pending",
    human_review_notes: str = "",
) -> int:
    """Collect four MCP tool results, assess the integration, and independently review it."""
    evidence_dir = evidence_dir.resolve()
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_directory = evidence_dir / "mcp" / run_id
    output_directory.mkdir(parents=True, exist_ok=True)
    evidence_path = output_directory / "evidence.json"
    report_paths = {
        "evidence": evidence_path,
        "runReport": output_directory / "run-report.md",
        "boundaryAnalysis": output_directory / "boundary-analysis.md",
        "integrationReport": output_directory / "integration-report.md",
        "toolReview": output_directory / "tool-review.md",
    }
    evidence: dict[str, Any] = {
        "schemaVersion": 2,
        "assessmentEvidence": True,
        "workflow": "MCP evidence collection -> implementation assessment -> independent review",
        "validationMode": "mcp",
        "runId": run_id,
        "recordedAtUtc": _now_iso(),
        "goal": goal,
        "serverUrl": server_url,
        "stages": [],
        "models": [],
        "serviceStartupChecks": [],
        "mcpEvidence": {},
        "checks": [],
        "implementationAssessment": {},
        "independentReview": {},
        "humanReview": {
            "decision": human_review_decision,
            "notes": human_review_notes,
        },
        "reportFiles": {
            name: str(path.relative_to(evidence_dir))
            for name, path in report_paths.items()
        },
        "privacy": {
            "sourceCodeSentToAi": False,
            "mcpExecutionEvidenceSentToAi": True,
            "apiKeysRecorded": False,
        },
    }

    def persist() -> None:
        temporary_path = evidence_path.with_suffix(".tmp")
        temporary_path.write_text(_json_text(evidence), encoding="utf-8")
        temporary_path.replace(evidence_path)

    def record_stage(name: str, status: str, details: str) -> None:
        evidence["stages"].append({
            "name": name,
            "status": status,
            "recordedAtUtc": _now_iso(),
            "details": details,
        })
        print(f"[MCP][{name}] {status.upper()}: {details}")
        persist()

    def call_model(
        stage: str,
        model: str,
        system_prompt: str,
        user_prompt: str,
        max_tokens: int = 1200,
    ) -> tuple[str | None, str | None]:
        try:
            output, error, metadata = ai_call(
                provider,
                model,
                system_prompt,
                user_prompt,
                max_tokens=max_tokens,
                temperature=0.1,
            )
        except Exception as exc:
            output, error, metadata = None, f"{type(exc).__name__}: {exc}", {}
        evidence["models"].append({
            "stage": stage,
            "requestedProvider": provider,
            "requestedModel": model,
            "usedProvider": metadata.get("usedProvider"),
            "usedModel": metadata.get("usedModel"),
            "systemPrompt": system_prompt,
            "userPrompt": user_prompt,
            "rawOutput": output,
            "error": error,
        })
        persist()
        return output, error

    def finish(reason: str | None = None) -> int:
        if reason:
            evidence["error"] = reason
        completed = {stage["name"] for stage in evidence["stages"]}
        for stage_name in MCP_STAGES:
            if stage_name not in completed:
                record_stage(stage_name, "skipped", reason or "Not reached.")

        stage_results = {
            stage["name"]: stage["status"] for stage in evidence["stages"]
        }
        startup_passed = bool(evidence["serviceStartupChecks"]) and all(
            check.get("passed") is True and not check.get("skipped", False)
            for check in evidence["serviceStartupChecks"]
        )
        collector_passed = evidence.get("mcpEvidence", {}).get("passed") is True
        evidence["passed"] = startup_passed and collector_passed and all(
            stage_results.get(name) == "passed" for name in MCP_STAGES
        )
        evidence["summary"] = "\n".join(
            f"{stage['name']}: {stage['status'].upper()}"
            for stage in evidence["stages"]
        )
        persist()
        try:
            _write_reports(evidence, output_directory)
        except Exception as exc:
            evidence["passed"] = False
            evidence["error"] = f"Could not write MCP reports: {exc}"
            persist()
            print(evidence["error"])
            return 1
        persist()
        print(f"\nMCP evidence written to: {evidence_path}")
        for name, path in report_paths.items():
            if name != "evidence":
                print(f"  {path}")
        return 0 if evidence["passed"] else 1

    print("[MCP][START] Starting MCP integration validation")
    try:
        evidence["serviceStartupChecks"] = start_services()
    except Exception as exc:
        evidence["serviceStartupChecks"] = [{
            "name": "Start MCP server",
            "passed": False,
            "skipped": False,
            "output": f"{type(exc).__name__}: {exc}",
        }]
    evidence["checks"].extend(evidence["serviceStartupChecks"])
    startup_passed = bool(evidence["serviceStartupChecks"]) and all(
        check.get("passed") is True and not check.get("skipped", False)
        for check in evidence["serviceStartupChecks"]
    )
    if not startup_passed:
        evidence["mcpEvidence"] = {
            "passed": False,
            "tools": [],
            "toolExecutions": {},
            "checks": [],
        }
        record_stage("OBSERVE", "failed", "MCP server did not become healthy; no tools were called.")
        return finish("MCP server startup failed; no tool execution evidence is available.")

    print("[MCP][OBSERVE] Collecting evidence")
    try:
        collector_evidence = McpCollector(server_url).collect_all()
    except Exception as exc:
        collector_evidence = {
            "passed": False,
            "tools": [],
            "toolExecutions": {},
            "checks": [],
            "error": f"{type(exc).__name__}: {exc}",
        }
    evidence["mcpEvidence"] = collector_evidence
    evidence["checks"].extend(collector_evidence.get("checks", []))
    executions = collector_evidence.get("toolExecutions", {})
    passed_tools = [
        tool_name for tool_name in LAB_REQUIRED_TOOLS
        if executions.get(tool_name, {}).get("passed") is True
    ]
    observation_summary = (
        f"{len(passed_tools)} of {len(LAB_REQUIRED_TOOLS)} required tool executions passed. "
        + "; ".join(
            f"{tool_name}={'PASS' if executions.get(tool_name, {}).get('passed') else 'FAIL / NOT RUN'}"
            for tool_name in LAB_REQUIRED_TOOLS
        )
    )
    record_stage(
        "OBSERVE",
        "passed" if collector_evidence.get("passed") else "failed",
        observation_summary,
    )

    implementation_prompt = build_implementation_prompt(goal, collector_evidence)
    print("[MCP][PROMPTS] Loaded MCP implementation assessment prompt")
    print("[MCP][LLM] Running MCP implementation model")
    implementation_output, implementation_error = call_model(
        "IMPLEMENTATION",
        implementation_model,
        "You are a precise MCP integration validator. Use only the supplied execution evidence.",
        implementation_prompt,
        max_tokens=300,
    )
    evidence["implementationAssessment"] = {
        "prompt": implementation_prompt,
        "output": implementation_output,
        "error": implementation_error,
    }
    if implementation_error or not implementation_output:
        record_stage(
            "IMPLEMENTATION",
            "failed",
            implementation_error or "The implementation assessment was empty.",
        )
        return finish("MCP implementation assessment failed.")
    record_stage(
        "IMPLEMENTATION",
        "passed",
        "Implementation model assessed the four-tool evidence and boundaries.",
    )

    review_prompt = build_integration_review_prompt(
        implementation_output,
        collector_evidence,
        evidence["humanReview"],
    )
    print("[MCP][PROMPTS] Loaded MCP integration review prompt")
    print("[MCP][LLM] Running MCP review model")
    review_output, review_error = call_model(
        "REVIEW",
        review_model,
        "You are an independent reviewer. Validate the implementation assessment against the captured MCP evidence only.",
        review_prompt,
        max_tokens=300,
    )
    evidence["independentReview"] = {
        "prompt": review_prompt,
        "output": review_output,
        "error": review_error,
    }
    if review_error or not review_output:
        record_stage("REVIEW", "failed", review_error or "The independent review was empty.")
        return finish("MCP independent review failed.")
    record_stage("REVIEW", "passed", "Independent review completed against the same execution evidence.")

    print("\n=== MCP Result ===")
    print(f"OBSERVE: {observation_summary}")
    print(f"IMPLEMENTATION: {implementation_output}")
    print(f"REVIEW: {review_output}")
    print("[MCP][DONE] Review complete")
    if not collector_evidence.get("passed"):
        return finish("One or more required MCP tool executions or result contracts failed.")
    return finish()