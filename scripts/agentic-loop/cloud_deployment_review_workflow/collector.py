from __future__ import annotations

import json
from pathlib import Path
from typing import Any


def _categorize(name: str) -> str:
    lowered = name.lower()
    if "portal" in lowered:
        return "portal"
    if "crud read" in lowered:
        return "frontend-to-api-to-database CRUD read"
    if "ai profile" in lowered:
        return "AI profile gate"
    return "other smoke check"


def collect_cloud_deployment_evidence(evidence_directory: Path) -> dict[str, Any]:
    evidence_directory = evidence_directory.expanduser().resolve()
    report_path = evidence_directory / "report.md"
    evidence_path = evidence_directory / "evidence.json"
    checks: list[dict[str, str]] = []
    records: Any = None
    report_text: str | None = None
    try:
        report_text = report_path.read_text(encoding="utf-8-sig")
    except FileNotFoundError:
        checks.append({"name": "smoke-test report availability", "status": "INCOMPLETE", "details": "report.md is missing."})
    except OSError as exc:
        checks.append({"name": "smoke-test report availability", "status": "INCOMPLETE", "details": f"report.md is unreadable ({type(exc).__name__})."})
    else:
        checks.append({"name": "smoke-test report availability", "status": "PASS", "details": "report.md is readable."})

    try:
        records = json.loads(evidence_path.read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        checks.append({"name": "smoke-test evidence availability", "status": "INCOMPLETE", "details": "evidence.json is missing."})
    except (OSError, json.JSONDecodeError) as exc:
        checks.append({"name": "smoke-test evidence availability", "status": "FAIL", "details": f"evidence.json is unreadable or malformed ({type(exc).__name__})."})
    else:
        if not isinstance(records, list):
            checks.append({"name": "smoke-test evidence structure", "status": "FAIL", "details": "Expected evidence.json to contain a list of check records."})
            records = None
        elif not records:
            checks.append({"name": "smoke-test evidence structure", "status": "INCOMPLETE", "details": "No check records were collected."})
        else:
            checks.append({"name": "smoke-test evidence structure", "status": "PASS", "details": f"Loaded {len(records)} smoke-test records."})

    sanitized_checks = []
    raw_rows = []
    if isinstance(records, list):
        for index, record in enumerate(records, start=1):
            if not isinstance(record, dict):
                checks.append({"name": f"smoke check {index} structure", "status": "FAIL", "details": "Check record is not an object."})
                continue
            name = record.get("Check", record.get("check"))
            result = record.get("Passed", record.get("passed"))
            http_status = record.get("Status", record.get("status"))
            if not isinstance(name, str) or not isinstance(result, bool) or http_status is None:
                checks.append({"name": f"smoke check {index} structure", "status": "FAIL", "details": "Check must include a name, actual status code/result, and boolean Passed value."})
                continue
            category = _categorize(name)
            state = "PASS" if result else "FAIL"
            safe_http_status = http_status if type(http_status) is int and 100 <= http_status <= 599 else "error" if http_status == "error" else "unknown"
            checks.append({"name": f"smoke check {index}: {category}", "status": state, "details": f"Reported HTTP/status value: {safe_http_status}; Passed={result}."})
            sanitized_checks.append({"index": index, "category": category, "status": safe_http_status, "passed": result})
            raw_rows.append({"index": index, "name": name, "status": http_status, "passed": result})

    if report_text is not None and isinstance(records, list):
        markdown_rows = sum(1 for line in report_text.splitlines() if line.startswith("|") and not line.startswith("|---") and "Check" not in line)
        if markdown_rows != len(records):
            checks.append({"name": "report/evidence consistency", "status": "INCOMPLETE", "details": f"Markdown rows={markdown_rows}; JSON evidence records={len(records)}."})
        else:
            checks.append({"name": "report/evidence consistency", "status": "PASS", "details": f"Both files contain {len(records)} check records."})

    checks.extend([
        {"name": "internal-only API/database coverage", "status": "INCOMPLETE", "details": "Not independently probed by the external smoke-test script; it documents that API/database apps are internal-only."},
        {"name": "broader deployment verification", "status": "INCOMPLETE", "details": "Smoke evidence does not establish untested flows, authorization for other identities, data integrity, or full release readiness."},
    ])
    status = "FAIL" if any(check["status"] == "FAIL" for check in checks) else "INCOMPLETE" if any(check["status"] == "INCOMPLETE" for check in checks) else "PASS"
    return {
        "sourceDirectory": str(evidence_directory),
        "sourceFiles": [str(report_path), str(evidence_path)],
        "smokeChecks": sanitized_checks,
        "checks": checks,
        "status": status,
        "scope": "External smoke checks cover the public portal, public frontend routes/proxy CRUD reads, and configured Financial Aid AI profile gates only. The script does not separately probe internal-only API/database apps.",
        "reportRows": raw_rows,
    }
