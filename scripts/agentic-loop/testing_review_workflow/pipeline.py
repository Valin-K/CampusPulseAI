from __future__ import annotations

import json
from typing import Any


def _model_evidence(collected: dict[str, Any]) -> dict[str, Any]:
    precommit = collected.get("precommit", {})
    ci = collected.get("ci", {})
    return {
        "precommit": {
            "available": precommit.get("available"),
            "status": precommit.get("status"),
            "hooks": [{"id": hook.get("id"), "status": hook.get("status"), "exitCode": hook.get("exitCode")} for hook in precommit.get("hooks", [])],
        },
        "ci": {
            "available": ci.get("available"),
            "status": ci.get("status"),
            "totals": ci.get("totals"),
            "records": [{key: record.get(key) for key in ("status", "passed", "failed", "skipped", "total")} for record in ci.get("records", [])],
        },
        "deterministicChecks": [{"name": item.get("name"), "status": item.get("status")} for item in collected.get("checks", [])],
        "unverified": collected.get("unverified", []),
    }


def build_implementation_prompt(collected: dict[str, Any]) -> str:
    return "Assess the supplied pre-commit and post-commit CI results. Report findings, failed checks, missing evidence, and unverified scope. Deterministic failure or incompleteness cannot be overridden by a positive opinion.\n\n" + json.dumps(_model_evidence(collected), indent=2, ensure_ascii=False)


def build_review_prompt(collected: dict[str, Any], assessment: str | None) -> str:
    return (
        "Independently audit this Testing Review. Verify each claimed status against the deterministic summary, "
        "identify omitted failures or missing artifacts, and state what remains unverified. This is not human release approval.\n\n"
        f"Evidence:\n{json.dumps(_model_evidence(collected), indent=2, ensure_ascii=False)}\n\n"
        f"Implementation assessment (untrusted model output):\n{assessment or 'Unavailable.'}"
    )


def render_report(evidence: dict[str, Any]) -> str:
    collected = evidence["collectedEvidence"]
    checks = "\n".join(
        f"| {item['name']} | **{item['status']}** | {item['details']} |"
        for item in collected.get("checks", [])
    ) or "| None | **INCOMPLETE** | No deterministic checks were recorded. |"
    precommit = collected.get("precommit", {})
    ci = collected.get("ci", {})
    totals = ci.get("totals", {})
    failed = [record for record in ci.get("records", []) if record.get("status") == "FAIL" or record.get("failed", 0)]
    missing = [item for item in collected.get("checks", []) if item.get("status") == "INCOMPLETE"]
    outputs = "\n\n".join(
        f"### {item['stage']} ({item['requestedModel']})\n\n{item['output'] or item['error'] or 'No output.'}"
        for item in evidence.get("models", [])
    )
    return f"""# Testing Review

- Run ID: `{evidence['runId']}`
- Deterministic review status: **{evidence['status']}**
- Human release decision: **pending**

## Pre-Commit Security Findings

Report status: **{precommit.get('status', 'missing')}**. Required hook evidence: {json.dumps(precommit.get('hooks', []), ensure_ascii=False)}

## Post-Commit CI Test Results

Report status: **{ci.get('status', 'missing')}**; counts: **{totals.get('passed', 0)} passed, {totals.get('failed', 0)} failed, {totals.get('skipped', 0)} skipped, {totals.get('total', 0)} total**.

Failed or failing test records: {json.dumps(failed, ensure_ascii=False) if failed else 'None reported by supplied TRX records.'}

## Missing Evidence and Deterministic Checks

| Check | Result | Details |
|---|---|---|
{checks}

Missing or incomplete items: {json.dumps(missing, ensure_ascii=False) if missing else 'None identified by the supplied report structures.'}

## Unverified

{chr(10).join(f'- {item}' for item in collected.get('unverified', [])) or '- No additional scope statement supplied.'}

## Assessment and Independent Review

{outputs or 'No model outputs were generated.'}

## Release Decision

A model opinion does not override a failed scan/test or missing evidence. This review does not approve a release; a human decision is **pending**.
"""
