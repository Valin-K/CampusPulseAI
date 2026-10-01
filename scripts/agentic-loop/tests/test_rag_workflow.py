from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIRECTORY))

from rag_workflow.collector import RagCollector
from rag_workflow.orchestrator import run_rag_workflow
from rag_workflow.pipeline import (
    build_implementation_prompt,
    build_review_prompt,
    build_review_system_prompt,
)
import agentic_loop


class FakeResponse:
    def __init__(self, status_code: int, body: object) -> None:
        self.status_code = status_code
        self.body = body
        self.text = str(body)

    def json(self) -> object:
        return self.body


class RagCollectorTests(unittest.TestCase):
    def run_collector(
        self,
        answer_status: str = "answered",
        wrong_citation: bool = False,
        existing_collections: list[dict] | None = None,
        post_ingest_collections: list[dict] | None = None,
    ) -> tuple[dict, list[dict]]:
        requests_seen: list[dict] = []
        fixture_id: str | None = None
        marker: str | None = None
        collection_reads = 0

        def request_func(method: str, url: str, **kwargs: object) -> FakeResponse:
            nonlocal collection_reads, fixture_id, marker
            requests_seen.append({"method": method, "url": url, **kwargs})

            if method == "GET" and url.endswith("/health"):
                return FakeResponse(200, {
                    "status": "healthy",
                    "service": "rag",
                    "allowedCollections": ["coursemanagement"],
                })
            if method == "GET" and url.endswith("/api/rag/collections"):
                collection_reads += 1
                if collection_reads == 1:
                    return FakeResponse(200, existing_collections or [])
                return FakeResponse(200, post_ingest_collections or [{
                    "name": "coursemanagement",
                    "documents": 1,
                    "chunks": 1,
                }])
            if method == "POST" and url.endswith("/documents"):
                document = kwargs["json"]["documents"][0]
                fixture_id = document["id"]
                marker = document["text"].split("marker ", 1)[1].split(".", 1)[0]
                return FakeResponse(200, {
                    "collection": "coursemanagement",
                    "documents": 1,
                    "chunks": 1,
                })
            if method == "POST" and url.endswith("/answer"):
                return FakeResponse(200, {
                    "status": answer_status,
                    "answer": "The marker confirms grounded citation retrieval.",
                    "confidence": 0.86,
                    "sources": [{
                        "documentId": "another-document" if wrong_citation else fixture_id,
                        "rank": 1,
                        "chunkIndex": 0,
                        "score": 0.86,
                        "text": f"Fixture marker {marker} confirms grounded citation retrieval.",
                    }],
                })
            if method == "DELETE" and url.endswith("/api/rag/coursemanagement"):
                return FakeResponse(200, {
                    "collection": "coursemanagement",
                    "deleted": True,
                })
            raise AssertionError(f"Unexpected request: {method} {url}")

        evidence = RagCollector(
            "http://localhost:5267", request_func=request_func
        ).collect_all()
        return evidence, requests_seen

    def test_collects_cited_answer_and_removes_only_new_collection(self) -> None:
        evidence, requests_seen = self.run_collector()

        self.assertTrue(evidence["passed"])
        self.assertEqual(evidence["validationEvidence"]["answer"]["status"], "answered")
        self.assertTrue(evidence["checks"][-1]["passed"])
        cleanup = requests_seen[-1]
        self.assertEqual(cleanup["method"], "DELETE")
        self.assertEqual(
            cleanup["url"],
            "http://localhost:5267/api/rag/coursemanagement",
        )

    def test_non_answered_status_fails_and_still_cleans_collection(self) -> None:
        evidence, requests_seen = self.run_collector("generation_unavailable")

        self.assertFalse(evidence["passed"])
        answer_check = next(
            check for check in evidence["checks"]
            if check["name"] == "RAG retrieval and grounded answer"
        )
        self.assertFalse(answer_check["passed"])
        self.assertEqual(requests_seen[-1]["method"], "DELETE")
        self.assertTrue(evidence["checks"][-1]["passed"])

    def test_collection_changed_after_seed_is_not_deleted(self) -> None:
        evidence, requests_seen = self.run_collector(
            post_ingest_collections=[{
                "name": "coursemanagement",
                "documents": 2,
                "chunks": 2,
            }],
        )

        self.assertFalse(evidence["passed"])
        self.assertFalse(any(request["method"] == "DELETE" for request in requests_seen))
        cleanup_check = evidence["checks"][-1]
        self.assertEqual(cleanup_check["name"], "RAG validation corpus cleanup")
        self.assertIn("left it untouched", cleanup_check["output"])

    def test_answer_with_citation_to_another_document_fails(self) -> None:
        evidence, _ = self.run_collector(wrong_citation=True)

        self.assertFalse(evidence["passed"])
        answer_check = next(
            check for check in evidence["checks"]
            if check["name"] == "RAG retrieval and grounded answer"
        )
        self.assertFalse(answer_check["passed"])

    def test_no_unused_collection_fails_without_mutating_existing_collections(self) -> None:
        evidence, requests_seen = self.run_collector(
            existing_collections=[{
                "name": "coursemanagement",
                "documents": 1,
                "chunks": 1,
            }],
        )

        self.assertFalse(evidence["passed"])
        self.assertEqual(
            [request["method"] for request in requests_seen],
            ["GET", "GET"],
        )
        workspace_check = evidence["checks"][-1]
        self.assertEqual(workspace_check["name"], "RAG validation workspace")
        self.assertIn("left untouched", workspace_check["output"])


def passed_rag_evidence() -> dict:
    return {
        "passed": True,
        "healthEvidence": {
            "status": "healthy",
            "service": "rag",
            "allowedCollections": ["coursemanagement"],
        },
        "collectionEvidence": [],
        "validationEvidence": {
            "collection": "coursemanagement",
            "documentId": "agentic-loop-validation-sample",
            "question": "What does the fixture confirm?",
            "ingestion": {"documents": 1, "chunks": 1},
            "answer": {
                "status": "answered",
                "answer": "It confirms grounded citation retrieval.",
                "confidence": 0.86,
                "sources": [{
                    "documentId": "agentic-loop-validation-sample",
                    "rank": 1,
                    "chunkIndex": 0,
                    "score": 0.86,
                    "text": "The fixture confirms grounded citation retrieval.",
                }],
            },
            "cleanup": {"deleted": True},
        },
        "checks": [{
            "name": "RAG retrieval and grounded answer",
            "passed": True,
            "skipped": False,
            "output": "Fixture citation validated.",
        }],
    }


class RagPipelineTests(unittest.TestCase):
    def test_prompts_include_lab8_review_rubrics_and_collected_evidence(self) -> None:
        evidence = passed_rag_evidence()
        implementation = build_implementation_prompt("Validate RAG", evidence)
        review_system = build_review_system_prompt()
        review = build_review_prompt("Implementation assessment.", evidence)

        self.assertIn(".NET HTTP service", implementation)
        self.assertIn("agentic-loop-validation-sample", implementation)
        self.assertIn("Risk:", review_system)
        self.assertIn("Strengths:", review_system)
        self.assertIn("Implementation assessment.", review)
        self.assertIn("grounded citation retrieval", review)


class RagWorkflowTests(unittest.TestCase):
    def test_cli_validation_wrapper_uses_rag_workflow(self) -> None:
        args = SimpleNamespace(
            evidence_dir=Path("evidence"),
            goal="Validate RAG mode.",
        )
        with (
            patch("agentic_loop.start_validation_services", return_value=[]) as startup,
            patch("agentic_loop.run_rag_workflow", return_value=0) as workflow,
        ):
            exit_code = agentic_loop.run_validation_mode(args)

            workflow.call_args.kwargs["start_services"]()

        self.assertEqual(exit_code, 0)
        startup.assert_called_once_with("rag")
        self.assertEqual(
            workflow.call_args.kwargs["server_url"],
            agentic_loop.RAG_SERVER_URL,
        )

    def test_workflow_runs_two_models_and_writes_validation_report(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with patch("rag_workflow.orchestrator.RagCollector") as collector_type:
                collector_type.return_value.collect_all.return_value = passed_rag_evidence()
                outputs = iter([
                    "Assessment: The fixture was indexed and cited with a finite confidence score.",
                    "Risk: The fixture is narrow. Correction: None. Retest: Add no-answer coverage.\n"
                    "Strengths: Citation matched. Risks: Corpus breadth is untested. "
                    "Recommendations: Add representative benchmarks.",
                ])
                model_calls: list[tuple[tuple[object, ...], dict[str, object]]] = []

                def fake_ai_call(*args: object, **kwargs: object) -> tuple[str, None, dict[str, str]]:
                    model_calls.append((args, kwargs))
                    return next(outputs), None, {
                        "usedProvider": "test",
                        "usedModel": "fake",
                    }

                exit_code = run_rag_workflow(
                    server_url="http://localhost:5267",
                    evidence_dir=Path(temporary_directory),
                    start_services=lambda: [{
                        "name": "Start RAG server",
                        "passed": True,
                        "skipped": False,
                    }],
                    ai_call=fake_ai_call,
                    provider="test",
                    implementation_model="implementation",
                    review_model="reviewer",
                    goal="Validate the RAG evidence.",
                )

            run_directory = next((Path(temporary_directory) / "rag").iterdir())
            evidence = json.loads(
                (run_directory / "evidence.json").read_text(encoding="utf-8")
            )
            report = (run_directory / "rag-validation-report.md").read_text(
                encoding="utf-8"
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(
            [stage["name"] for stage in evidence["stages"]],
            ["OBSERVE", "IMPLEMENTATION", "REVIEW"],
        )
        self.assertTrue(evidence["passed"])
        self.assertEqual([model["stage"] for model in evidence["models"]], [
            "IMPLEMENTATION", "REVIEW",
        ])
        self.assertEqual(len(model_calls), 2)
        self.assertEqual(
            [call[1]["max_tokens"] for call in model_calls],
            [300, 300],
        )
        self.assertIn("grounded citation retrieval", report)
        self.assertIn("Independent Review", report)
        self.assertFalse(evidence["privacy"]["sourceCodeSentToAi"])

    def test_startup_failure_is_recorded_and_skips_models(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with (
                patch("rag_workflow.orchestrator.RagCollector") as collector_type,
                patch("builtins.print"),
            ):
                exit_code = run_rag_workflow(
                    server_url="http://localhost:5267",
                    evidence_dir=Path(temporary_directory),
                    start_services=lambda: [{
                        "name": "Start AI Gateway",
                        "passed": False,
                        "skipped": False,
                        "output": "Bind for 127.0.0.1:5269 failed: port is already allocated",
                    }],
                    ai_call=lambda *args, **kwargs: self.fail("AI must not run after startup failure"),
                    provider="test",
                    implementation_model="implementation",
                    review_model="reviewer",
                )

            run_directory = next((Path(temporary_directory) / "rag").iterdir())
            evidence = json.loads(
                (run_directory / "evidence.json").read_text(encoding="utf-8")
            )
            report = (run_directory / "rag-validation-report.md").read_text(
                encoding="utf-8"
            )

        self.assertEqual(exit_code, 1)
        self.assertFalse(evidence["passed"])
        self.assertEqual(evidence["models"], [])
        self.assertIn("port is already allocated", report)
        collector_type.assert_not_called()


if __name__ == "__main__":
    unittest.main()