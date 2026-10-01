from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

from .collector import RagCollector
from .pipeline import (
    build_implementation_prompt,
    build_review_prompt,
    build_review_system_prompt,
)


RAG_STAGES = ("OBSERVE", "IMPLEMENTATION", "REVIEW")
AiCall = Callable[..., tuple[str | None, str | None, dict[str, Any]]]


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _json_text(value: Any) -> str:
    return json.dumps(value, indent=2, ensure_ascii=False)


def _observation_summary(evidence: dict[str, Any]) -> str:
    checks = evidence.get("checks", [])
    passed_count = sum(1 for check in checks if check.get("passed"))
    validation = evidence.get("validationEvidence", {})
    retrieval = validation.get("retrieval")
    retrieval_metrics = validation.get("retrievalMetrics", {})
    answer = validation.get("answer")
    negative_answer = validation.get("negativeAnswer")
    answer_status = answer.get("status", "not captured") if isinstance(answer, dict) else "not captured"
    negative_status = (
        negative_answer.get("status", "not captured")
        if isinstance(negative_answer, dict)
        else "not captured"
    )
    cosine_similarity = (
        retrieval.get("cosineSimilarity") if isinstance(retrieval, dict) else None
    )
    return (
        f"{passed_count} of {len(checks)} evidence checks passed; "
        f"cosine similarity={cosine_similarity}; "
        f"P@5={retrieval_metrics.get('pAt5')}; "
        f"R@5={retrieval_metrics.get('rAt5')}; "
        f"answer status={answer_status}; negative-query status={negative_status}."
    )


def _render_report(evidence: dict[str, Any]) -> str:
    stages = "\n".join(
        f"| {stage['name']} | {stage['status'].upper()} | "
        f"{stage['details'].replace('|', '\\|').replace(chr(10), ' ')} |"
        for stage in evidence["stages"]
    )
    checks = "\n".join(
        f"| {check['name'].replace('|', '\\|')} | "
        f"{'PASS' if check.get('passed') else 'FAIL / NOT RUN'} | "
        f"{check.get('output', '').replace('|', '\\|').replace(chr(10), ' ')} |"
        for check in evidence.get("checks", [])
    ) or "| None | NOT RUN | No RAG evidence was collected. |"
    rag_evidence = evidence.get("ragEvidence", {})
    assessment = evidence.get("implementationAssessment", {}).get("output") or "Not generated."
    review = evidence.get("independentReview", {}).get("output") or "Not generated."
    improvement = evidence.get("improvement", {})
    return f"""# RAG Validation Report

- Run ID: `{evidence['runId']}`
- Recorded: `{evidence['recordedAtUtc']}`
- Result: **{'PASS' if evidence.get('passed') else 'FAIL'}**
- Goal: {evidence['goal']}

## Workflow Stages

| Stage | Status | Evidence |
|---|---|---|
{stages}

## OBSERVE: Collected Evidence

| Check | Result | Details |
|---|---|---|
{checks}

```json
{_json_text(rag_evidence.get('validationEvidence', {}))}
```

## IMPLEMENTATION: Assessment

{assessment}

## REVIEW: Independent Review

{review}

## IMPROVE: Recorded Follow-Up

{improvement.get('recommendations', 'No recommendations recorded.')}

- Status: {improvement.get('status', 'Not reached.')}
- Source changes applied automatically: **{'yes' if improvement.get('sourceChangesApplied') else 'no'}**
- Next step: Apply an accepted correction and rerun validation.

## Human Review

- Decision: **{evidence['humanReview']['decision']}**
- Notes: {evidence['humanReview']['notes'] or 'Pending human review.'}

## Privacy

Only the synthetic validation fixture and its response are supplied to the models. Source code and API keys are not recorded or sent.
"""


def run_rag_workflow(
    *,
    server_url: str,
    evidence_dir: Path,
    start_services: Callable[[], list[dict[str, Any]]],
    ai_call: AiCall,
    provider: str,
    implementation_model: str,
    review_model: str,
    goal: str = "Validate and improve the CampusPulseAI RAG integration.",
) -> int:
    """Collect RAG execution evidence, assess it, and independently review it."""
    evidence_dir = evidence_dir.resolve()
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_directory = evidence_dir / "rag" / run_id
    output_directory.mkdir(parents=True, exist_ok=True)
    evidence_path = output_directory / "evidence.json"
    report_path = output_directory / "rag-validation-report.md"
    evidence: dict[str, Any] = {
        "schemaVersion": 1,
        "assessmentEvidence": True,
        "workflow": "RAG evidence collection -> implementation assessment -> independent review",
        "validationMode": "rag",
        "runId": run_id,
        "recordedAtUtc": _now_iso(),
        "goal": goal,
        "serverUrl": server_url,
        "stages": [],
        "models": [],
        "serviceStartupChecks": [],
        "ragEvidence": {},
        "checks": [],
        "implementationAssessment": {},
        "independentReview": {},
        "humanReview": {"decision": "pending", "notes": ""},
        "reportFiles": {
            "evidence": evidence_path.relative_to(evidence_dir).as_posix(),
            "validationReport": report_path.relative_to(evidence_dir).as_posix(),
        },
        "privacy": {
            "sourceCodeSentToAi": False,
            "ragExecutionEvidenceSentToAi": True,
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
        print(f"[RAG][{name}] {status.upper()}: {details}")
        persist()

    def call_model(
        stage: str,
        model: str,
        system_prompt: str,
        user_prompt: str,
    ) -> tuple[str | None, str | None]:
        try:
            output, error, metadata = ai_call(
                provider,
                model,
                system_prompt,
                user_prompt,
                max_tokens=1200,
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
        for stage_name in RAG_STAGES:
            if stage_name not in completed:
                record_stage(stage_name, "skipped", reason or "Not reached.")

        stage_results = {
            stage["name"]: stage["status"] for stage in evidence["stages"]
        }
        startup_checks = evidence["serviceStartupChecks"]
        startup_passed = bool(startup_checks) and all(
            check.get("passed") is True and not check.get("skipped", False)
            for check in startup_checks
        )
        collector_passed = evidence.get("ragEvidence", {}).get("passed") is True
        evidence["passed"] = startup_passed and collector_passed and all(
            stage_results.get(name) == "passed" for name in RAG_STAGES
        )
        evidence["summary"] = "\n".join(
            f"{stage['name']}: {stage['status'].upper()}"
            for stage in evidence["stages"]
        )
        persist()
        try:
            report_path.write_text(_render_report(evidence), encoding="utf-8")
        except Exception as exc:
            evidence["passed"] = False
            evidence["error"] = f"Could not write RAG validation report: {exc}"
            persist()
            print(evidence["error"])
            return 1
        persist()
        print(f"\nRAG evidence written to: {evidence_path}")
        print(f"RAG validation report written to: {report_path}")
        return 0 if evidence["passed"] else 1

    print("[RAG][START] Starting RAG validation")
    try:
        evidence["serviceStartupChecks"] = start_services()
    except Exception as exc:
        evidence["serviceStartupChecks"] = [{
            "name": "Start RAG services",
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
        details = "; ".join(
            f"{check.get('name', 'service startup')}: "
            f"{check.get('output', 'failed')}"
            for check in evidence["serviceStartupChecks"]
        ) or "No service startup checks were returned."
        record_stage("OBSERVE", "failed", details)
        return finish("RAG service startup failed; evidence collection did not run.")

    print("[RAG][OBSERVE] Collecting execution evidence")
    try:
        rag_evidence = RagCollector(server_url).collect_all()
    except Exception as exc:
        rag_evidence = {
            "passed": False,
            "healthEvidence": None,
            "collectionEvidence": None,
            "validationEvidence": {},
            "checks": [],
            "error": f"{type(exc).__name__}: {exc}",
        }
    evidence["ragEvidence"] = rag_evidence
    evidence["checks"].extend(rag_evidence.get("checks", []))
    observation_summary = _observation_summary(rag_evidence)
    record_stage(
        "OBSERVE",
        "passed" if rag_evidence.get("passed") else "failed",
        observation_summary,
    )

    try:
        implementation_prompt = build_implementation_prompt(goal, rag_evidence)
        implementation_system_prompt = (
            "You are a precise RAG implementation assessment agent. "
            "Use only the supplied execution evidence. Assess; do not write code."
        )
        print("[RAG][PROMPTS] Loaded RAG implementation prompt")
        print("[RAG][LLM] Running RAG implementation model")
        implementation_output, implementation_error = call_model(
            "IMPLEMENTATION",
            implementation_model,
            implementation_system_prompt,
            implementation_prompt,
        )
    except Exception as exc:
        implementation_output = None
        implementation_error = f"{type(exc).__name__}: {exc}"
    evidence["implementationAssessment"] = {
        "output": implementation_output,
        "error": implementation_error,
    }
    if implementation_error or not implementation_output:
        record_stage(
            "IMPLEMENTATION",
            "failed",
            implementation_error or "The implementation assessment was empty.",
        )
        return finish("RAG implementation assessment failed.")
    record_stage(
        "IMPLEMENTATION",
        "passed",
        "Implementation model assessed the collected RAG execution evidence.",
    )

    try:
        review_system_prompt = build_review_system_prompt()
        review_prompt = build_review_prompt(implementation_output, rag_evidence)
        print("[RAG][PROMPTS] Loaded RAG review and reasoning prompts")
        print("[RAG][LLM] Running RAG review model")
        review_output, review_error = call_model(
            "REVIEW",
            review_model,
            review_system_prompt,
            review_prompt,
        )
    except Exception as exc:
        review_output = None
        review_error = f"{type(exc).__name__}: {exc}"
    evidence["independentReview"] = {
        "output": review_output,
        "error": review_error,
    }
    if review_error or not review_output:
        record_stage(
            "REVIEW",
            "failed",
            review_error or "The independent review was empty.",
        )
        return finish("RAG independent review failed.")
    evidence["improvement"] = {
        "status": "recommendations_recorded",
        "recommendations": review_output,
        "sourceChangesApplied": False,
    }
    persist()
    record_stage(
        "REVIEW",
        "passed",
        "Independent review completed against the same collected evidence.",
    )

    print("\n=== RAG Result ===")
    print(f"OBSERVE: {observation_summary}")
    print(f"IMPLEMENTATION: {implementation_output}")
    print(f"REVIEW: {review_output}")
    print("[RAG][DONE] Review complete")
    if not rag_evidence.get("passed"):
        return finish("One or more RAG evidence checks failed.")
    return finish()