from __future__ import annotations

import argparse
import json
import os
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_OUTPUT_DIRECTORY = REPO_ROOT / "docs" / "release-2" / "testing-evidence"
EXPECTED_ARTIFACTS = {
    "student-1-test-results": "student-1-ci",
    "student-3-test-results": "student-3",
    "student-5-test-results": "student-5-ci",
    "shared-backend-test-results": "shared-backend-ci",
}


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def _workflow_and_artifact(trx_path: Path, input_directory: Path) -> tuple[str, str]:
    relative_parts = trx_path.relative_to(input_directory).parts
    artifact = next(
        (
            part
            for part in relative_parts
            if part.endswith("-test-results")
        ),
        "unknown-artifact",
    )
    workflow = next(
        (
            part
            for part in relative_parts
            if (part.startswith("student-") or part == "shared-backend")
            and part != artifact
        ),
        "unknown",
    )
    if workflow == "unknown" and artifact in EXPECTED_ARTIFACTS:
        workflow = EXPECTED_ARTIFACTS[artifact]
    elif workflow.startswith("student-") and workflow in {"student-3"}:
        workflow = "student-3"
    return workflow, artifact


def _int_counter(counters: ET.Element, *names: str) -> int | None:
    attributes = {_local_name(key).lower(): value for key, value in counters.attrib.items()}
    for name in names:
        raw = attributes.get(name.lower())
        if raw is not None:
            try:
                return int(raw)
            except ValueError:
                return None
    return None


def parse_trx(path: Path) -> dict[str, Any]:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exc:
        return {"status": "INCOMPLETE", "error": f"Unreadable TRX: {exc}"}

    counters = next((node for node in root.iter() if _local_name(node.tag) == "Counters"), None)
    unit_results = [node for node in root.iter() if _local_name(node.tag) == "UnitTestResult"]
    if not unit_results and counters is None:
        return {"status": "INCOMPLETE", "error": "TRX contains neither test results nor summary counters."}

    outcome_counts: dict[str, int] = {}
    for result in unit_results:
        outcome = next(
            (value for key, value in result.attrib.items() if _local_name(key).lower() == "outcome"),
            "Unknown",
        )
        normalized = outcome.strip().lower()
        outcome_counts[normalized] = outcome_counts.get(normalized, 0) + 1

    if counters is not None:
        passed = _int_counter(counters, "passed")
        failed = sum(_int_counter(counters, key) or 0 for key in ("failed", "error", "timeout", "aborted"))
        skipped = sum(_int_counter(counters, key) or 0 for key in ("notExecuted", "inconclusive", "notRunnable", "passedButNotRunnable", "pending", "disconnected"))
        total = _int_counter(counters, "total")
        if passed is None:
            passed = outcome_counts.get("passed", 0)
        if failed == 0:
            failed = sum(count for name, count in outcome_counts.items() if name in {"failed", "error", "timeout", "aborted"})
        if skipped == 0:
            skipped = sum(count for name, count in outcome_counts.items() if name in {"notexecuted", "inconclusive", "notrunnable", "pending", "disconnected"})
        total = total if total is not None else passed + failed + skipped
    else:
        passed = outcome_counts.get("passed", 0)
        failed = sum(count for name, count in outcome_counts.items() if name in {"failed", "error", "timeout", "aborted"})
        skipped = sum(count for name, count in outcome_counts.items() if name in {"notexecuted", "inconclusive", "notrunnable", "pending", "disconnected"})
        total = len(unit_results)

    if total == 0 and not unit_results:
        status = "INCOMPLETE"
        error = "TRX summary reports no tests."
    else:
        status = "FAIL" if failed > 0 else "PASS"
        error = None
    return {
        "status": status,
        "total": total,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "outcomes": outcome_counts,
        "error": error,
    }


def collect_ci_reports(
    artifact_directory: Path,
    output_directory: Path = DEFAULT_OUTPUT_DIRECTORY,
    commit_sha: str | None = None,
) -> dict[str, Any]:
    artifact_directory = artifact_directory.expanduser().resolve()
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    trx_files = sorted(artifact_directory.rglob("*.trx")) if artifact_directory.is_dir() else []
    records = []
    for trx_path in trx_files:
        parsed = parse_trx(trx_path)
        workflow, artifact = _workflow_and_artifact(trx_path, artifact_directory)
        records.append({
            "file": str(trx_path),
            "workflow": workflow,
            "artifact": artifact,
            "testProject": trx_path.stem,
            "commitSha": commit_sha or os.getenv("GITHUB_SHA"),
            **parsed,
        })

    discovered_artifacts = {record["artifact"] for record in records}
    for artifact, workflow in EXPECTED_ARTIFACTS.items():
        if artifact not in discovered_artifacts:
            records.append({
                "file": None,
                "workflow": workflow,
                "artifact": artifact,
                "testProject": None,
                "commitSha": commit_sha or os.getenv("GITHUB_SHA"),
                "status": "INCOMPLETE",
                "total": 0,
                "passed": 0,
                "failed": 0,
                "skipped": 0,
                "error": "Expected workflow artifact is absent from the supplied download directory.",
            })

    if not records:
        records.append({
            "file": None,
            "workflow": "unknown",
            "artifact": "unknown-artifact",
            "testProject": None,
            "commitSha": commit_sha or os.getenv("GITHUB_SHA"),
            "status": "INCOMPLETE",
            "total": 0,
            "passed": 0,
            "failed": 0,
            "skipped": 0,
            "error": f"No TRX artifacts found in {artifact_directory}.",
        })

    statuses = {record["status"] for record in records}
    overall_status = "FAIL" if "FAIL" in statuses else "INCOMPLETE" if "INCOMPLETE" in statuses else "PASS"
    report = {
        "schemaVersion": 1,
        "mode": "ci-test-report",
        "runId": run_id,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "status": overall_status,
        "artifactDirectory": str(artifact_directory),
        "commitSha": commit_sha or os.getenv("GITHUB_SHA"),
        "records": records,
        "totals": {
            "passed": sum(record.get("passed", 0) for record in records),
            "failed": sum(record.get("failed", 0) for record in records),
            "skipped": sum(record.get("skipped", 0) for record in records),
            "total": sum(record.get("total", 0) for record in records),
        },
    }

    output_directory = output_directory.expanduser().resolve()
    output_directory.mkdir(parents=True, exist_ok=True)
    (output_directory / "ci-test-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    lines = [
        "# Post-Commit CI Test Report",
        "",
        f"- Status: **{overall_status}**",
        f"- Generated (UTC): `{report['generatedAtUtc']}`",
        f"- Artifact directory: `{artifact_directory}`",
        f"- Commit SHA: `{report['commitSha'] or 'unavailable'}`",
        "",
        "| Workflow | Artifact | Test project/file | Result | Passed | Failed | Skipped | Total | Details |",
        "|---|---|---|---|---:|---:|---:|---:|---|",
    ]
    for record in records:
        details = (record.get("error") or record.get("file") or "").replace("|", "\\|")
        lines.append(
            f"| {record['workflow']} | {record['artifact']} | {record.get('testProject') or 'missing'} "
            f"| **{record['status']}** | {record.get('passed', 0)} | {record.get('failed', 0)} "
            f"| {record.get('skipped', 0)} | {record.get('total', 0)} | {details} |"
        )
    totals = report["totals"]
    lines.extend([
        "",
        f"Totals: **{totals['passed']} passed, {totals['failed']} failed, {totals['skipped']} skipped, {totals['total']} total**.",
        "",
        "A PASS means only that the supplied TRX files report no failed tests; it does not establish that all CI workflows ran or that the release is approved.",
        "",
    ])
    (output_directory / "ci-test-report.md").write_text("\n".join(lines), encoding="utf-8")
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Convert downloaded .NET TRX artifacts into reviewable reports.")
    parser.add_argument("artifact_directory", type=Path, help="Directory containing downloaded CI artifact files.")
    parser.add_argument("--output-directory", type=Path, default=DEFAULT_OUTPUT_DIRECTORY)
    parser.add_argument("--commit-sha", help="Commit SHA associated with the downloaded artifacts, when known.")
    args = parser.parse_args(argv)
    report = collect_ci_reports(args.artifact_directory, args.output_directory, args.commit_sha)
    print(f"CI test report status: {report['status']}")
    print(f"Reports written to {args.output_directory.resolve()}")
    return 1 if report["status"] == "FAIL" else 0 if report["status"] == "PASS" else 2


if __name__ == "__main__":
    sys.exit(main())
