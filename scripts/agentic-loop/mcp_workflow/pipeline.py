from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .collector import LAB_REQUIRED_TOOLS


PROMPT_DIRECTORY = Path(__file__).resolve().parents[1] / "prompts" / "lab7"


def load_prompt(relative_path: str) -> str:
    prompt_path = PROMPT_DIRECTORY / relative_path
    return prompt_path.read_text(encoding="utf-8")


def lab_tools(tools: list[dict[str, Any]]) -> list[dict[str, Any]]:
    by_name = {
        tool["name"].casefold(): tool
        for tool in tools
        if isinstance(tool, dict) and isinstance(tool.get("name"), str)
    }
    return [
        by_name[name.casefold()]
        for name in LAB_REQUIRED_TOOLS
        if name.casefold() in by_name
    ]


def _lab_evidence(evidence: dict[str, Any]) -> dict[str, Any]:
    required_tools = lab_tools(evidence.get("tools", []))
    executions = evidence.get("toolExecutions", {})
    return {
        "passed": evidence.get("passed", False),
        "missingRequiredTools": evidence.get("missingRequiredTools", []),
        "registeredToolCount": len(evidence.get("tools", [])),
        "healthEvidence": evidence.get("healthEvidence"),
        "requiredToolContracts": required_tools,
        "toolExecutions": {
            tool_name: executions.get(tool_name, {
                "executed": False,
                "passed": False,
                "error": "No execution evidence was captured.",
            })
            for tool_name in LAB_REQUIRED_TOOLS
        },
        "checks": evidence.get("checks", []),
    }


def build_implementation_prompt(goal: str, evidence: dict[str, Any]) -> str:
    template = load_prompt("implementation/tool_selection_prompt.txt")
    return (
        template
        .replace("{{GOAL}}", goal)
        .replace(
            "{{MCP_EVIDENCE}}",
            json.dumps(_lab_evidence(evidence), indent=2, ensure_ascii=False),
        )
    )


def build_integration_review_prompt(
    implementation_output: str,
    evidence: dict[str, Any],
    human_review: dict[str, Any],
) -> str:
    template = load_prompt("review/integration_review_prompt.txt")
    return (
        template
        .replace(
            "{{MCP_EVIDENCE}}",
            json.dumps(_lab_evidence(evidence), indent=2, ensure_ascii=False),
        )
        .replace("{{IMPLEMENTATION_ASSESSMENT}}", implementation_output)
        .replace(
            "{{HUMAN_REVIEW}}",
            json.dumps(human_review, indent=2, ensure_ascii=False),
        )
    )


def render_boundary_analysis(evidence: dict[str, Any]) -> str:
    tools = lab_tools(evidence.get("tools", []))
    executions = evidence.get("toolExecutions", {})
    rows = []
    for tool in tools:
        description = " ".join(tool["description"].split()).replace("|", "\\|")
        execution = executions.get(tool["name"], {})
        status = "PASS" if execution.get("passed") else "FAIL / NOT RUN"
        arguments = json.dumps(execution.get("arguments", {}), ensure_ascii=False)
        rows.append(
            f"| `{tool['name']}` | {tool['owner']} | {description} | "
            f"`{arguments}` | {status} |"
        )
    table = "\n".join(rows) or "| None | | No live Lab 07 tools were validated. | `{}` | NOT RUN |"
    return f"""# MCP Tool Boundary Analysis

## Live Lab 07 Tool Executions

| Tool | Owner | Declared responsibility | Validation arguments | Result |
|---|---|---|---|---|
{table}

## Decision

The collector invokes the four Lab 07 tools using documented, bounded validation arguments. Other registered CampusPulseAI tools are outside this MCP integration run. Each result is checked against the live tool's expected response contract before it is supplied to the implementation and review models.
"""