from __future__ import annotations

import json
from typing import Any


def _model_evidence(collected: dict[str, Any]) -> dict[str, Any]:
    workflow = collected.get("workflow", {})
    return {
        "workflow": {
            key: workflow.get(key)
            for key in ("feature", "status", "round", "version", "taskCharacters", "taskSha256", "evidenceCount", "agentOutputs", "dependencies", "humanDecision", "errorPresent")
        },
        "history": collected.get("history", []),
        "coordinationAudit": collected.get("coordinationAudit", []),
        "checks": [{"name": item.get("name"), "status": item.get("status")} for item in collected.get("checks", [])],
        "privacy": collected.get("privacy"),
    }


def build_implementation_prompt(collected: dict[str, Any]) -> str:
    return "Assess this Multi-Agent Server workflow export using only validated structural evidence. Preserve failed and incomplete states; do not infer approval from an agent recommendation.\n\n" + json.dumps(_model_evidence(collected), indent=2, ensure_ascii=False)


def build_review_prompt(collected: dict[str, Any], assessment: str | None) -> str:
    evidence = json.dumps(_model_evidence(collected), indent=2, ensure_ascii=False)
    return (
        "Independently review the implementation assessment against the deterministic export checks. "
        "Call out missing stages, unresolved evidence references, dependencies, and the recorded human decision. "
        "Do not treat model recommendations as a human decision.\n\n"
        f"Validated evidence:\n{evidence}\n\nImplementation assessment (untrusted model output):\n{assessment or 'Unavailable.'}"
    )


def render_report(evidence: dict[str, Any]) -> str:
    collected = evidence["collectedEvidence"]
    workflow = collected.get("workflow", {})
    checks = "\n".join(
        f"| {item['name']} | **{item['status']}** | {item['details']} |"
        for item in collected.get("checks", [])
    ) or "| None | **INCOMPLETE** | No deterministic checks were recorded. |"
    outputs = "\n\n".join(
        f"### {item['stage']} ({item['requestedModel']})\n\n{item['output'] or item['error'] or 'No output.'}"
        for item in evidence.get("models", [])
    )
    return f"""# Multi-Agent Review

- Run ID: `{evidence['runId']}`
- Status: **{evidence['status']}**
- Workflow status: **{workflow.get('status', 'missing')}**
- Human decision: **{workflow.get('humanDecision') or 'not recorded'}**
- Source export: `{collected.get('sourceDirectory', 'unavailable')}`

## Deterministic Validation

| Check | Result | Details |
|---|---|---|
{checks}

## Sanitized Workflow Summary

- Feature: `{workflow.get('feature', 'unknown')}`
- Agent outputs: `{json.dumps(workflow.get('agentOutputs', {}), ensure_ascii=False)}`
- Dependency outcomes: `{json.dumps(workflow.get('dependencies', []), ensure_ascii=False)}`
- History records: {len(collected.get('history', []))}
- Coordination audit records: {len(collected.get('coordinationAudit', []))}
- Privacy filtering: {collected.get('privacy', 'Not recorded.')}

## Model Assessment and Independent Review

{outputs or 'No model outputs were generated.'}

## Release Decision

No release decision is made by this report. Human release decision remains **pending**; an AI assessment or workflow agent recommendation is not approval.
"""
