from __future__ import annotations

import json
from pathlib import Path
from typing import Any


REQUIRED_HOOKS = {"detect-secrets", "dotnet-security-analyzers", "nuget-vulnerable-packages"}


def _read_json(path: Path) -> tuple[Any | None, str | None]:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig")), None
    except FileNotFoundError:
        return None, "missing"
    except (OSError, json.JSONDecodeError) as exc:
        return None, type(exc).__name__


def collect_testing_evidence(precommit_report: Path, ci_report: Path) -> dict[str, Any]:
    precommit_report = precommit_report.expanduser().resolve()
    ci_report = ci_report.expanduser().resolve()
    checks: list[dict[str, str]] = []
    precommit, precommit_error = _read_json(precommit_report)
    ci, ci_error = _read_json(ci_report)
    sanitized_precommit: dict[str, Any] = {"available": False, "hooks": []}
    sanitized_ci: dict[str, Any] = {"available": False, "records": [], "totals": {}}

    if precommit_error:
        checks.append({"name": "pre-commit report availability", "status": "INCOMPLETE", "details": f"Pre-commit report {precommit_error}: {precommit_report}"})
    elif not isinstance(precommit, dict) or not isinstance(precommit.get("hooks"), list):
        checks.append({"name": "pre-commit report structure", "status": "FAIL", "details": "Report must be an object containing a hooks list."})
    else:
        hooks = precommit["hooks"]
        by_id = {hook.get("id"): hook for hook in hooks if isinstance(hook, dict) and isinstance(hook.get("id"), str)}
        missing = sorted(REQUIRED_HOOKS - set(by_id))
        checks.append({"name": "pre-commit hook coverage", "status": "INCOMPLETE" if missing else "PASS", "details": "Missing hooks: " + (", ".join(missing) if missing else "none")})
        sanitized_hooks = []
        for hook_id in sorted(REQUIRED_HOOKS):
            hook = by_id.get(hook_id)
            status = hook.get("status") if hook else None
            valid_status = isinstance(status, str) and status in {"PASS", "FAIL"}
            check_status = "FAIL" if status == "FAIL" else "INCOMPLETE" if not valid_status else "PASS"
            checks.append({"name": f"security hook {hook_id}", "status": check_status, "details": f"Reported status: {status or 'missing'}."})
            sanitized_hooks.append({"id": hook_id, "status": status if valid_status else "MISSING_OR_INVALID", "exitCode": hook.get("exitCode") if hook else None})
        sanitized_precommit = {
            "available": True,
            "status": precommit.get("status") if isinstance(precommit.get("status"), str) and precommit.get("status") in {"PASS", "FAIL"} else "MISSING_OR_INVALID",
            "commitSha": precommit.get("commitSha"),
            "hooks": sanitized_hooks,
        }

    if ci_error:
        checks.append({"name": "CI test report availability", "status": "INCOMPLETE", "details": f"CI report {ci_error}: {ci_report}"})
    elif not isinstance(ci, dict) or not isinstance(ci.get("records"), list) or not isinstance(ci.get("totals"), dict):
        checks.append({"name": "CI test report structure", "status": "FAIL", "details": "Report must contain records and totals objects."})
    else:
        records = ci["records"]
        if not records:
            checks.append({"name": "CI artifact coverage", "status": "INCOMPLETE", "details": "No CI test records were reported."})
        sanitized_records = []
        for index, record in enumerate(records, start=1):
            if not isinstance(record, dict):
                checks.append({"name": f"CI record {index}", "status": "FAIL", "details": "Record is not an object."})
                continue
            record_status = record.get("status")
            counts_valid = all(isinstance(record.get(key), int) and record[key] >= 0 for key in ("passed", "failed", "skipped", "total"))
            if record_status == "FAIL" or (type(record.get("failed")) is int and record["failed"] > 0):
                state = "FAIL"
            elif record_status != "PASS" or not counts_valid:
                state = "INCOMPLETE"
            else:
                state = "PASS"
            checks.append({"name": f"CI test record {index}", "status": state, "details": f"{record.get('workflow', 'unknown')} / {record.get('artifact', 'unknown')}: {record.get('passed', 0)} passed, {record.get('failed', 0)} failed, {record.get('skipped', 0)} skipped."})
            sanitized_records.append({
                "workflow": record.get("workflow"),
                "artifact": record.get("artifact"),
                "testProject": record.get("testProject"),
                "status": record_status if isinstance(record_status, str) and record_status in {"PASS", "FAIL", "INCOMPLETE"} else "MISSING_OR_INVALID",
                "passed": record.get("passed"),
                "failed": record.get("failed"),
                "skipped": record.get("skipped"),
                "total": record.get("total"),
            })
        totals = ci.get("totals", {})
        totals_valid = all(isinstance(totals.get(key), int) and totals[key] >= 0 for key in ("passed", "failed", "skipped", "total"))
        totals_status = "FAIL" if type(totals.get("failed")) is int and totals["failed"] > 0 else "PASS" if totals_valid else "INCOMPLETE"
        checks.append({"name": "CI aggregate totals", "status": totals_status, "details": f"Aggregate: {totals.get('passed', 0)} passed, {totals.get('failed', 0)} failed, {totals.get('skipped', 0)} skipped, {totals.get('total', 0)} total."})
        sanitized_ci = {
            "available": True,
            "status": ci.get("status") if isinstance(ci.get("status"), str) and ci.get("status") in {"PASS", "FAIL", "INCOMPLETE"} else "MISSING_OR_INVALID",
            "commitSha": ci.get("commitSha"),
            "records": sanitized_records,
            "totals": {key: totals.get(key) for key in ("passed", "failed", "skipped", "total")},
        }

    status = "FAIL" if any(check["status"] == "FAIL" for check in checks) else "INCOMPLETE" if any(check["status"] == "INCOMPLETE" for check in checks) else "PASS"
    return {
        "sourceFiles": [str(precommit_report), str(ci_report)],
        "precommit": sanitized_precommit,
        "ci": sanitized_ci,
        "checks": checks,
        "status": status,
        "unverified": ["A passing model assessment does not replace deterministic hook/test results or human release approval.", "Only supplied TRX artifacts are reviewed; workflows without downloaded artifacts remain unverified."],
    }
