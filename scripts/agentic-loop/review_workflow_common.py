from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable


AiCall = Callable[..., tuple[str | None, str | None, dict[str, Any]]]


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")


def run_review_workflow(
    *,
    mode: str,
    output_root: Path,
    collector: Callable[[], dict[str, Any]],
    build_implementation_prompt: Callable[[dict[str, Any]], str],
    build_review_prompt: Callable[[dict[str, Any], str | None], str],
    render_report: Callable[[dict[str, Any]], str],
    ai_call: AiCall,
    provider: str,
    implementation_model: str,
    review_model: str,
) -> int:
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_directory = output_root.expanduser().resolve() / mode / run_id
    output_directory.mkdir(parents=True, exist_ok=True)
    evidence_path = output_directory / "evidence.json"
    report_path = output_directory / "review-report.md"
    validation_path = output_directory / "validation-log.json"
    started_at = _now_iso()
    try:
        collected = collector()
    except Exception as exc:
        collected = {
            "sourceFiles": [],
            "checks": [{"name": "evidence collection", "status": "INCOMPLETE", "details": "Collector failed before producing evidence."}],
            "status": "INCOMPLETE",
            "collectorError": f"{type(exc).__name__}: {exc}",
        }

    model_calls: list[dict[str, Any]] = []

    def call_model(stage: str, model: str, system_prompt: str, user_prompt: str) -> str | None:
        try:
            output, error, metadata = ai_call(
                provider,
                model,
                system_prompt,
                user_prompt,
                max_tokens=2000,
                temperature=0.1,
            )
        except Exception as exc:
            output, error, metadata = None, f"{type(exc).__name__}: {exc}", {}
        model_calls.append({
            "stage": stage,
            "requestedProvider": provider,
            "requestedModel": model,
            "usedProvider": metadata.get("usedProvider") if isinstance(metadata, dict) else None,
            "usedModel": metadata.get("usedModel") if isinstance(metadata, dict) else None,
            "systemPrompt": system_prompt,
            "userPrompt": user_prompt,
            "output": output,
            "error": error,
        })
        _write_json(evidence_path, {
            "schemaVersion": 1,
            "runId": run_id,
            "timestampUtc": started_at,
            "mode": mode,
            "sourceEvidencePaths": collected.get("sourceFiles", []),
            "deterministicChecks": collected.get("checks", []),
            "collectedEvidence": collected,
            "models": model_calls,
            "status": "INCOMPLETE",
        })
        return output

    implementation_prompt = build_implementation_prompt(collected)
    implementation_output = call_model(
        "implementation-assessment",
        implementation_model,
        "Assess only the supplied evidence. Separate verified facts, failures, gaps, and limitations. Do not approve a release.",
        implementation_prompt,
    )
    review_prompt = build_review_prompt(collected, implementation_output)
    independent_review = call_model(
        "independent-review",
        review_model,
        "Independently verify the assessment against the deterministic evidence. Identify unsupported claims and missing checks. This is not human release approval.",
        review_prompt,
    )

    deterministic_status = collected.get("status", "INCOMPLETE")
    model_errors = [item["error"] for item in model_calls if item.get("error")]
    if deterministic_status == "FAIL":
        final_status = "FAIL"
    elif deterministic_status != "PASS" or model_errors or any(item.get("output") is None for item in model_calls):
        final_status = "INCOMPLETE"
    else:
        final_status = "PASS"

    evidence = {
        "schemaVersion": 1,
        "runId": run_id,
        "timestampUtc": started_at,
        "completedAtUtc": _now_iso(),
        "mode": mode,
        "sourceEvidencePaths": collected.get("sourceFiles", []),
        "deterministicChecks": collected.get("checks", []),
        "collectedEvidence": collected,
        "models": model_calls,
        "errors": model_errors + ([collected["collectorError"]] if collected.get("collectorError") else []),
        "status": final_status,
        "humanReleaseDecision": "pending",
        "releaseApproval": False,
    }
    validation_log = {
        "schemaVersion": 1,
        "runId": run_id,
        "timestampUtc": started_at,
        "completedAtUtc": evidence["completedAtUtc"],
        "mode": mode,
        "sourceEvidencePaths": evidence["sourceEvidencePaths"],
        "checks": evidence["deterministicChecks"],
        "modelCalls": [
            {"stage": item["stage"], "requestedModel": item["requestedModel"], "usedModel": item["usedModel"], "error": item["error"]}
            for item in model_calls
        ],
        "errors": evidence["errors"],
        "status": final_status,
        "humanReleaseDecision": "pending",
        "releaseApproval": False,
    }
    _write_json(evidence_path, evidence)
    _write_json(validation_path, validation_log)
    report_path.write_text(render_report(evidence), encoding="utf-8")
    print(f"[{mode}] {final_status}: {report_path}")
    return 0 if final_status == "PASS" else 1 if final_status == "FAIL" else 2
