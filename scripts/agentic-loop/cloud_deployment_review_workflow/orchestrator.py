from __future__ import annotations

from pathlib import Path
from typing import Any, Callable

from review_workflow_common import run_review_workflow
from .collector import collect_cloud_deployment_evidence
from .pipeline import build_implementation_prompt, build_review_prompt, render_report


def run_cloud_deployment_review_workflow(
    *,
    deployment_evidence_directory: Path,
    evidence_dir: Path,
    ai_call: Callable[..., tuple[str | None, str | None, dict[str, Any]]],
    provider: str,
    implementation_model: str,
    review_model: str,
) -> int:
    return run_review_workflow(
        mode="cloud-deployment-review",
        output_root=evidence_dir,
        collector=lambda: collect_cloud_deployment_evidence(deployment_evidence_directory),
        build_implementation_prompt=build_implementation_prompt,
        build_review_prompt=build_review_prompt,
        render_report=render_report,
        ai_call=ai_call,
        provider=provider,
        implementation_model=implementation_model,
        review_model=review_model,
    )
