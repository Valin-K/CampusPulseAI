from __future__ import annotations

import json
from typing import Any


SCOPE = "The external smoke test does not independently probe the internal-only API/database apps. It verifies only the public endpoints and configured checks represented by the supplied smoke-test evidence."


def _model_evidence(collected: dict[str, Any]) -> dict[str, Any]:
    return {
        "smokeChecks": collected.get("smokeChecks", []),
        "deterministicChecks": [{"name": item.get("name"), "status": item.get("status")} for item in collected.get("checks", [])],
        "scope": SCOPE,
    }


def build_implementation_prompt(collected: dict[str, Any]) -> str:
    return "Review the supplied cloud deployment smoke evidence. Distinguish passed checks, failed checks, and untested scope. Do not claim internal-only API/database apps were independently probed.\n\n" + json.dumps(_model_evidence(collected), indent=2, ensure_ascii=False)


def build_review_prompt(collected: dict[str, Any], assessment: str | None) -> str:
    return (
        "Independently verify the cloud deployment assessment against the actual smoke-test result booleans. "
        "Call out any overclaim about coverage, particularly internal-only API/database apps. This is not release approval.\n\n"
        f"Evidence:\n{json.dumps(_model_evidence(collected), indent=2, ensure_ascii=False)}\n\n"
        f"Implementation assessment (untrusted model output):\n{assessment or 'Unavailable.'}"
    )


def render_report(evidence: dict[str, Any]) -> str:
    collected = evidence["collectedEvidence"]
    rows = "\n".join(
        f"| {item['index']} | {item['name']} | {item['status']} | **{'PASS' if item['passed'] else 'FAIL'}** |"
        for item in collected.get("reportRows", [])
    ) or "| — | No valid source check records | — | **INCOMPLETE** |"
    checks = "\n".join(
        f"| {item['name']} | **{item['status']}** | {item['details']} |"
        for item in collected.get("checks", [])
    )
    outputs = "\n\n".join(
        f"### {item['stage']} ({item['requestedModel']})\n\n{item['output'] or item['error'] or 'No output.'}"
        for item in evidence.get("models", [])
    )
    return f"""# Cloud Deployment Review

- Run ID: `{evidence['runId']}`
- Status: **{evidence['status']}**
- Source evidence: `{collected.get('sourceDirectory', 'unavailable')}`

## Actual Smoke-Test Results

| # | Check | Status | Result |
|---:|---|---:|---|
{rows}

## Deterministic Validation

| Validation | Result | Details |
|---|---|---|
{checks}

## Coverage Limits

{collected.get('scope', SCOPE)}

A smoke test through a public frontend's proxy is not an independent external probe of an internal-only API or database app. This evidence also does not establish untested application flows or general release readiness.

## Assessment and Independent Review

{outputs or 'No model outputs were generated.'}

## Release Decision

This report records evidence only. AI review is not human approval; the human release decision remains **pending**.
"""
