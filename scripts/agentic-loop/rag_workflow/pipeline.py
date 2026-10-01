from __future__ import annotations

import json
from pathlib import Path
from typing import Any


PROMPT_DIRECTORY = Path(__file__).resolve().parents[1] / "prompts" / "lab8"


def load_prompt(relative_path: str) -> str:
    return (PROMPT_DIRECTORY / relative_path).read_text(encoding="utf-8")


def _assessment_evidence(evidence: dict[str, Any]) -> dict[str, Any]:
    validation = evidence.get("validationEvidence", {})
    return {
        "passed": evidence.get("passed", False),
        "error": evidence.get("error"),
        "checks": [
            {
                "name": check.get("name"),
                "passed": check.get("passed"),
                "output": check.get("output"),
            }
            for check in evidence.get("checks", [])
        ],
        "healthEvidence": evidence.get("healthEvidence"),
        "collectionEvidence": evidence.get("collectionEvidence"),
        "validationEvidence": {
            key: validation.get(key)
            for key in (
                "collection",
                "documentId",
                "documentIds",
                "expectedRelevantChunks",
                "question",
                "negativeQuestion",
                "ingestion",
                "retrieval",
                "retrievalMetrics",
                "answer",
                "negativeAnswer",
                "cleanup",
            )
            if key in validation
        },
    }


def build_implementation_prompt(goal: str, evidence: dict[str, Any]) -> str:
    task_prompt = load_prompt("implementation/rag_implementation_prompt.txt")
    return f"""{task_prompt}

Validation goal:
{goal}

Observed RAG execution evidence (JSON):
{json.dumps(_assessment_evidence(evidence), indent=2, ensure_ascii=False)}

Assess only what this evidence supports. Do not treat the Lab's Python files or MCP tool names as requirements for this .NET HTTP service. Reply in at most 40 words.""".strip()


def build_review_system_prompt() -> str:
    quality_prompt = load_prompt("review/rag_review_prompt.txt")
    reasoning_prompt = load_prompt("review/rag_reasoning_prompt.txt")
    return f"""{quality_prompt}

{reasoning_prompt}

Treat these as two review sections and return both formats in order: Risk/Correction/Retest, then Strengths/Risks/Recommendations. Use at most 80 words total.""".strip()


def build_review_prompt(
    implementation_output: str,
    evidence: dict[str, Any],
) -> str:
    return f"""Implementation assessment:
{implementation_output}

Observed RAG execution evidence (JSON):
{json.dumps(_assessment_evidence(evidence), indent=2, ensure_ascii=False)}

Review the assessment against the evidence only. Flag unsupported claims and missing validation.""".strip()