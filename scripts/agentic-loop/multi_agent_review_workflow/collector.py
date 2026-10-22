from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any


WORKFLOW_STATUSES = {"running", "awaiting_human_review", "approved", "rejected", "failed"}
AGENT_ROLES = ("planner", "worker", "reviewer")
FEATURES = {"academicrecords", "coursemanagement", "campushousing", "financialaidbilling", "eventscheduling"}
DECISIONS = {"approve", "approved", "accept", "accepted", "reject", "rejected", "correct"}
DEPENDENCY_STATUSES = {"available", "unavailable", "skipped", "not_requested", "healthy", "failed", "error", "disabled", "fallback"}
REQUIRED_EXPORT_FILES = ("workflow.json", "workflow_history.jsonl", "coordination_audit.jsonl")


def missing_multi_agent_export_files(export_directory: Path) -> list[str]:
    directory = export_directory.expanduser().resolve()
    return [name for name in REQUIRED_EXPORT_FILES if not (directory / name).is_file()]


def _digest(value: Any) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False).encode("utf-8")).hexdigest()


def _load_json(path: Path) -> tuple[Any | None, str | None]:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig")), None
    except FileNotFoundError:
        return None, "missing"
    except (OSError, json.JSONDecodeError) as exc:
        return None, type(exc).__name__


def _read_jsonl(path: Path) -> tuple[list[Any], list[str]]:
    values: list[Any] = []
    errors: list[str] = []
    try:
        lines = path.read_text(encoding="utf-8-sig").splitlines()
    except FileNotFoundError:
        return values, ["missing"]
    except OSError as exc:
        return values, [type(exc).__name__]
    for line_number, line in enumerate(lines, start=1):
        if not line.strip():
            continue
        try:
            values.append(json.loads(line))
        except json.JSONDecodeError:
            errors.append(f"line {line_number}: malformed JSON")
    return values, errors


def _check(name: str, status: str, details: str) -> dict[str, str]:
    return {"name": name, "status": status, "details": details}


def _safe_enum(value: Any, allowed: set[str], default: str = "unknown") -> str:
    return value if isinstance(value, str) and value in allowed else default


def _safe_integer(value: Any) -> int | None:
    return value if type(value) is int else None


def _status(checks: list[dict[str, str]], workflow_status: str | None) -> str:
    if workflow_status in {"failed", "rejected"} or any(item["status"] == "FAIL" for item in checks):
        return "FAIL"
    if workflow_status in {None, "running", "awaiting_human_review"} or any(item["status"] == "INCOMPLETE" for item in checks):
        return "INCOMPLETE"
    return "PASS"


def collect_multi_agent_evidence(export_directory: Path) -> dict[str, Any]:
    export_directory = export_directory.expanduser().resolve()
    checks: list[dict[str, str]] = []
    workflow_path = export_directory / "workflow.json"
    history_path = export_directory / "workflow_history.jsonl"
    audit_path = export_directory / "coordination_audit.jsonl"
    raw_workflow, workflow_error = _load_json(workflow_path)
    history, history_errors = _read_jsonl(history_path)
    audit, audit_errors = _read_jsonl(audit_path)

    workflow: dict[str, Any] | None = None
    if isinstance(raw_workflow, dict):
        wrapped = raw_workflow.get("workflow")
        workflow = wrapped if isinstance(wrapped, dict) else raw_workflow
        checks.append(_check("workflow.json structure", "PASS" if isinstance(workflow, dict) else "FAIL", "Workflow snapshot parsed."))
    else:
        state = "INCOMPLETE" if workflow_error == "missing" else "FAIL"
        checks.append(_check("workflow.json structure", state, "Workflow snapshot is missing or malformed."))

    for name, records, errors in (
        ("workflow_history.jsonl", history, history_errors),
        ("coordination_audit.jsonl", audit, audit_errors),
    ):
        if errors:
            checks.append(_check(name, "INCOMPLETE" if errors == ["missing"] else "FAIL", "; ".join(errors)))
        elif not records:
            checks.append(_check(name, "INCOMPLETE", "No JSONL records were present."))
        else:
            checks.append(_check(name, "PASS", f"Parsed {len(records)} records."))

    sanitized_workflow: dict[str, Any] = {}
    workflow_status: str | None = None
    if workflow:
        workflow_status = workflow.get("status") if isinstance(workflow.get("status"), str) else None
        if workflow_status not in WORKFLOW_STATUSES:
            checks.append(_check("workflow status", "FAIL" if workflow_status else "INCOMPLETE", "Missing or unknown workflow status."))
        else:
            status_check = "FAIL" if workflow_status in {"failed", "rejected"} else "INCOMPLETE" if workflow_status in {"running", "awaiting_human_review"} else "PASS"
            checks.append(_check("workflow status", status_check, f"Export status is {workflow_status}."))

        evidence = workflow.get("evidence") if isinstance(workflow.get("evidence"), list) else []
        evidence_ids = {item.get("id") for item in evidence if isinstance(item, dict) and isinstance(item.get("id"), str)}
        agents = workflow.get("agents") if isinstance(workflow.get("agents"), list) else []
        role_details: dict[str, dict[str, Any]] = {}
        unresolved_references = 0
        for role in AGENT_ROLES:
            executions = [agent for agent in agents if isinstance(agent, dict) and str(agent.get("agent", "")).lower() == role]
            execution = executions[-1] if executions else None
            output_name = {"planner": "plan", "worker": "work", "reviewer": "review"}[role]
            output = execution.get(output_name) if isinstance(execution, dict) else None
            if not isinstance(output, dict):
                role_details[role] = {"present": False}
                continue
            refs = output.get({"worker": "evidenceUsed", "reviewer": "evidenceChecked"}.get(role, ""), [])
            if role in {"worker", "reviewer"}:
                if not isinstance(refs, list):
                    unresolved_references += 1
                else:
                    unresolved_references += sum(1 for ref in refs if not isinstance(ref, str) or ref not in evidence_ids)
                    if not refs:
                        unresolved_references += 1
            if role == "planner":
                valid_output = isinstance(output.get("objective"), str) and bool(output.get("objective", "").strip()) and isinstance(output.get("steps"), list) and bool(output.get("steps"))
            elif role == "worker":
                valid_output = isinstance(output.get("output"), str) and bool(output.get("output", "").strip()) and isinstance(refs, list) and bool(refs)
            else:
                valid_output = _safe_enum(output.get("recommendation"), {"approve", "correct", "reject"}) != "unknown" and isinstance(refs, list) and bool(refs)
            serialized = json.dumps(output, ensure_ascii=False)
            role_details[role] = {
                "present": True,
                "valid": valid_output,
                "outputCharacters": len(serialized),
                "outputSha256": _digest(output),
                "evidenceReferenceCount": len(refs) if isinstance(refs, list) else None,
            }

        invalid_roles = [role for role in AGENT_ROLES if not role_details.get(role, {}).get("valid")]
        checks.append(_check("agent stages and outputs", "FAIL" if invalid_roles else "PASS", "Missing or incomplete outputs: " + (", ".join(invalid_roles) if invalid_roles else "none")))
        checks.append(_check("agent evidence references", "FAIL" if unresolved_references else "PASS", f"Unresolved or malformed evidence references: {unresolved_references}."))

        dependencies = workflow.get("dependencies") if isinstance(workflow.get("dependencies"), list) else []
        dependency_rows = []
        for dependency in dependencies:
            if isinstance(dependency, dict):
                dep_status = dependency.get("status")
                status_value = dep_status.lower() if isinstance(dep_status, str) else ""
                dependency_rows.append({"service": "redacted", "status": status_value if status_value in DEPENDENCY_STATUSES else "unknown"})
        unknown_dependencies = sum(1 for item in dependency_rows if item["status"] == "unknown")
        checks.append(_check("dependency outcomes", "INCOMPLETE" if unknown_dependencies or not dependencies else "PASS", f"Recorded {len(dependency_rows)} dependency outcomes; {unknown_dependencies} have no status."))

        human_reviews = workflow.get("humanReviews") if isinstance(workflow.get("humanReviews"), list) else []
        last_review = human_reviews[-1] if human_reviews and isinstance(human_reviews[-1], dict) else None
        decision = last_review.get("decision") if last_review else None
        safe_decision = _safe_enum(decision, DECISIONS)
        if workflow_status == "approved" and safe_decision not in {"approve", "approved", "accept", "accepted"}:
            checks.append(_check("human decision", "INCOMPLETE", "Workflow is approved but no matching human approval record was found."))
        elif workflow_status == "awaiting_human_review" or not decision:
            checks.append(_check("human decision", "INCOMPLETE", "No completed human decision is recorded."))
        elif safe_decision in {"reject", "rejected"}:
            checks.append(_check("human decision", "FAIL", "A human rejection is recorded."))
        else:
            checks.append(_check("human decision", "PASS", f"Human decision is {safe_decision}."))

        task = workflow.get("task")
        sanitized_workflow = {
            "idSha256": _digest(workflow.get("id")) if isinstance(workflow.get("id"), str) else None,
            "feature": _safe_enum(workflow.get("feature"), FEATURES),
            "status": workflow_status,
            "round": _safe_integer(workflow.get("round")),
            "version": _safe_integer(workflow.get("version")),
            "taskCharacters": len(task) if isinstance(task, str) else None,
            "taskSha256": _digest(task) if isinstance(task, str) else None,
            "evidenceCount": len(evidence),
            "evidenceReferences": "IDs and content withheld; only reference integrity was checked.",
            "agentOutputs": role_details,
            "dependencies": dependency_rows,
            "humanDecision": _safe_enum(decision, DECISIONS, "") or None,
            "humanCommentCharacters": len(last_review.get("comments", "")) if last_review and isinstance(last_review.get("comments"), str) else None,
            "errorPresent": bool(workflow.get("error")),
        }

    sanitized_history = []
    for row in history:
        if isinstance(row, dict):
            status_value = row.get("status")
            sanitized_history.append({"status": _safe_enum(status_value, WORKFLOW_STATUSES), "round": _safe_integer(row.get("round")), "version": _safe_integer(row.get("version")), "agents": len(row.get("agents", [])) if isinstance(row.get("agents"), list) else None})
        else:
            sanitized_history.append({"recordType": type(row).__name__})
    sanitized_audit = []
    for row in audit:
        if isinstance(row, dict):
            status_value = row.get("status")
            event_value = row.get("event")
            sanitized_audit.append({
                "sequence": _safe_integer(row.get("sequence")),
                "event": event_value if isinstance(event_value, str) and re.fullmatch(r"[a-z_]{1,40}", event_value) else "redacted",
                "status": _safe_enum(status_value, WORKFLOW_STATUSES),
                "version": _safe_integer(row.get("version")),
                "round": _safe_integer(row.get("round")),
            })
        else:
            sanitized_audit.append({"recordType": type(row).__name__})

    return {
        "sourceDirectory": str(export_directory),
        "sourceFiles": [str(workflow_path), str(history_path), str(audit_path)],
        "workflow": sanitized_workflow,
        "history": sanitized_history,
        "coordinationAudit": sanitized_audit,
        "checks": checks,
        "status": _status(checks, workflow_status),
        "privacy": "Free text, evidence content/IDs, agent outputs, comments, dependency details, and errors are withheld. Models receive structural metadata and hashes only.",
    }
