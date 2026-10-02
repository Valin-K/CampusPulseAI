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
from rag_workflow.orchestrator import (
    _replace_evidence_file,
    run_rag_workflow,
)
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
        negative_answer_status: str = "insufficient_context",
        wrong_citation: bool = False,
        existing_collections: list[dict] | None = None,
        post_ingest_collections: list[dict] | None = None,
        retrieval_document_indexes: list[int] | None = None,
    ) -> tuple[dict, list[dict]]:
        requests_seen: list[dict] = []
        fixture_id: str | None = None
        marker: str | None = None
        collection_reads = 0
        answer_calls = 0
        fixture_documents: list[dict] = []

        def request_func(method: str, url: str, **kwargs: object) -> FakeResponse:
            nonlocal answer_calls, collection_reads, fixture_documents, fixture_id, marker
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
                    "documents": len(fixture_documents),
                    "chunks": len(fixture_documents),
                }])
            if method == "POST" and url.endswith("/documents"):
                fixture_documents = kwargs["json"]["documents"]
                document = fixture_documents[0]
                fixture_id = document["id"]
                marker = document["text"].split("marker ", 1)[1].split(".", 1)[0]
                return FakeResponse(200, {
                    "collection": "coursemanagement",
                    "documents": len(fixture_documents),
                    "chunks": len(fixture_documents),
                })
            if method == "POST" and url.endswith("/retrieve"):
                payload = kwargs["json"]
                retrieved_documents = (
                    [
                        fixture_documents[index]
                        for index in retrieval_document_indexes
                    ]
                    if retrieval_document_indexes is not None
                    else fixture_documents[:5]
                )
                sources = [{
                    "rank": index + 1,
                    "documentId": document["id"],
                    "chunkIndex": 0,
                    "score": 0.86 if index == 0 else 0.1,
                    "text": document["text"],
                } for index, document in enumerate(retrieved_documents)]
                return FakeResponse(200, {
                    "collection": "coursemanagement",
                    "question": payload["question"],
                    "topK": payload["topK"],
                    "cosineSimilarity": 0.86,
                    "sources": sources,
                })
            if method == "POST" and url.endswith("/answer"):
                answer_calls += 1
                is_positive = answer_calls == 1
                status = answer_status if is_positive else negative_answer_status
                return FakeResponse(200, {
                    "status": status,
                    "answer": "The marker identifies a blue lantern."
                    if is_positive else "The knowledge base does not contain enough information.",
                    "confidence": 0.86 if is_positive else 0.12,
                    "sources": [{
                        "documentId": "another-document"
                        if wrong_citation and is_positive else fixture_id,
                        "rank": 1,
                        "chunkIndex": 0,
                        "score": 0.86 if is_positive else 0.12,
                        "text": fixture_documents[0]["text"],
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
        self.assertEqual(
            evidence["validationEvidence"]["expectedRelevantChunks"],
            3,
        )
        self.assertEqual(
            len(evidence["validationEvidence"]["relevantDocumentIds"]),
            3,
        )
        self.assertEqual(
            evidence["validationEvidence"]["retrievalMetrics"]["pAt5"],
            0.6,
        )
        self.assertEqual(
            evidence["validationEvidence"]["retrievalMetrics"]["rAt5"],
            1.0,
        )
        self.assertEqual(
            evidence["validationEvidence"]["retrievalMetrics"]["relevantRetrieved"],
            3,
        )
        self.assertEqual(
            evidence["validationEvidence"]["negativeAnswer"]["status"],
            "insufficient_context",
        )
        self.assertIn(
            "municipal bylaw",
            evidence["validationEvidence"]["negativeQuestion"],
        )
        self.assertTrue(evidence["checks"][-1]["passed"])
        cleanup = requests_seen[-1]
        self.assertEqual(cleanup["method"], "DELETE")
        self.assertEqual(
            cleanup["url"],
            "http://localhost:5267/api/rag/coursemanagement",
        )

    def test_retrieval_requires_all_known_relevant_chunks(self) -> None:
        evidence, _ = self.run_collector(
            retrieval_document_indexes=[0, 3, 4, 5, 6],
        )

        self.assertFalse(evidence["passed"])
        retrieval_check = next(
            check for check in evidence["checks"]
            if check["name"] == "RAG retrieval metrics"
        )
        self.assertFalse(retrieval_check["passed"])
        metrics = evidence["validationEvidence"]["retrievalMetrics"]
        self.assertEqual(metrics["relevantRetrieved"], 1)
        self.assertEqual(metrics["relevantTotal"], 3)
        self.assertAlmostEqual(metrics["rAt5"], 1 / 3)

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
                "documents": 9,
                "chunks": 9,
            }],
        )

        self.assertFalse(evidence["passed"])
        self.assertFalse(any(request["method"] == "DELETE" for request in requests_seen))
        cleanup_check = evidence["checks"][-1]
        self.assertEqual(cleanup_check["name"], "RAG validation corpus cleanup")
        self.assertIn("left it untouched", cleanup_check["output"])

    def test_negative_query_must_be_abstained(self) -> None:
        evidence, _ = self.run_collector(negative_answer_status="answered")

        self.assertFalse(evidence["passed"])
        negative_check = next(
            check for check in evidence["checks"]
            if check["name"] == "RAG negative-query abstention"
        )
        self.assertFalse(negative_check["passed"])

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
            "documentId": "agentic-loop-validation-sample-1",
            "documentIds": [
                "agentic-loop-validation-sample-1",
                "agentic-loop-validation-sample-2",
                "agentic-loop-validation-sample-3",
                "agentic-loop-validation-decoy-1",
                "agentic-loop-validation-decoy-2",
                "agentic-loop-validation-decoy-3",
                "agentic-loop-validation-decoy-4",
                "agentic-loop-validation-decoy-5",
            ],
            "relevantDocumentIds": [
                "agentic-loop-validation-sample-1",
                "agentic-loop-validation-sample-2",
                "agentic-loop-validation-sample-3",
            ],
            "relevantFixtures": [
                {
                    "documentId": "agentic-loop-validation-sample-1",
                    "text": "The fixture confirms grounded citation retrieval.",
                },
                {
                    "documentId": "agentic-loop-validation-sample-2",
                    "text": "The fixture confirms complete recall.",
                },
                {
                    "documentId": "agentic-loop-validation-sample-3",
                    "text": "The fixture confirms grounded abstention behavior.",
                },
            ],
            "expectedRelevantChunks": 3,
            "question": "What does the fixture confirm?",
            "negativeQuestion": "What does the unrelated query ask?",
            "ingestion": {"documents": 8, "chunks": 8},
            "retrieval": {
                "cosineSimilarity": 0.86,
                "sources": [
                    {
                        "documentId": "agentic-loop-validation-sample-1",
                        "rank": 1,
                        "chunkIndex": 0,
                        "score": 0.86,
                        "text": "The fixture confirms grounded citation retrieval.",
                    },
                    {
                        "documentId": "agentic-loop-validation-sample-2",
                        "rank": 2,
                        "chunkIndex": 0,
                        "score": 0.82,
                        "text": "The fixture confirms complete recall.",
                    },
                    {
                        "documentId": "agentic-loop-validation-sample-3",
                        "rank": 3,
                        "chunkIndex": 0,
                        "score": 0.78,
                        "text": "The fixture confirms grounded abstention behavior.",
                    },
                    {
                        "documentId": "agentic-loop-validation-decoy-1",
                        "rank": 4,
                        "chunkIndex": 0,
                        "score": 0.3,
                        "text": "An unrelated fixture passage.",
                    },
                    {
                        "documentId": "agentic-loop-validation-decoy-2",
                        "rank": 5,
                        "chunkIndex": 0,
                        "score": 0.2,
                        "text": "Another unrelated fixture passage.",
                    },
                ],
            },
            "retrievalMetrics": {
                "k": 5,
                "retrievedCount": 5,
                "relevantRetrieved": 3,
                "relevantTotal": 3,
                "pAt5": 0.6,
                "rAt5": 1.0,
                "cosineSimilarity": 0.86,
            },
            "answer": {
                "status": "answered",
                "answer": "It confirms grounded citation retrieval.",
                "confidence": 0.86,
                "sources": [{
                    "documentId": "agentic-loop-validation-sample-1",
                    "rank": 1,
                    "chunkIndex": 0,
                    "score": 0.86,
                    "text": "The fixture confirms grounded citation retrieval.",
                }],
            },
            "negativeAnswer": {
                "status": "insufficient_context",
                "confidence": 0.12,
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
        self.assertIn("retrievalMetrics", implementation)
        self.assertIn("negativeAnswer", implementation)
        self.assertIn("not calibrated", implementation)
        self.assertIn("Risk:", review_system)
        self.assertIn("Strengths:", review_system)
        self.assertIn("P@5 and R@5", review_system)
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
        self.assertIn("P@5=0.6", evidence["stages"][0]["details"])
        self.assertIn("R@5=1.0", evidence["stages"][0]["details"])
        self.assertTrue(evidence["passed"])
        self.assertEqual([model["stage"] for model in evidence["models"]], [
            "IMPLEMENTATION", "REVIEW",
        ])
        self.assertEqual(len(model_calls), 2)
        self.assertEqual(
            [call[1]["max_tokens"] for call in model_calls],
            [1200, 2400],
        )
        self.assertEqual(
            [model["maxTokens"] for model in evidence["models"]],
            [1200, 2400],
        )
        self.assertIn("grounded citation retrieval", report)
        self.assertIn("Independent Review", report)
        self.assertIn("IMPROVE: Recorded Follow-Up", report)
        self.assertIn("Source changes applied automatically: **no**", report)
        self.assertFalse(evidence["privacy"]["sourceCodeSentToAi"])
        self.assertEqual(
            evidence["improvement"]["status"],
            "recommendations_recorded",
        )

    def test_evidence_replace_retries_transient_permission_error(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            temporary_path = directory / "evidence.tmp"
            evidence_path = directory / "evidence.json"
            temporary_path.write_text("new evidence", encoding="utf-8")
            evidence_path.write_text("old evidence", encoding="utf-8")
            original_replace = Path.replace
            attempts = 0

            def fail_once(source: Path, target: Path) -> Path:
                nonlocal attempts
                attempts += 1
                if attempts == 1:
                    raise PermissionError(5, "Access is denied", str(target))
                return original_replace(source, target)

            with (
                patch.object(Path, "replace", new=fail_once),
                patch("rag_workflow.orchestrator.time.sleep") as sleep,
            ):
                _replace_evidence_file(temporary_path, evidence_path)

            self.assertEqual(attempts, 2)
            self.assertEqual(evidence_path.read_text(encoding="utf-8"), "new evidence")
            self.assertFalse(temporary_path.exists())
            sleep.assert_called_once_with(0.1)

    def test_evidence_replace_reports_persistent_permission_error(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            temporary_path = directory / "evidence.tmp"
            evidence_path = directory / "evidence.json"
            temporary_path.write_text("latest evidence", encoding="utf-8")
            evidence_path.write_text("previous evidence", encoding="utf-8")

            with (
                patch.object(
                    Path,
                    "replace",
                    side_effect=PermissionError(5, "Access is denied"),
                ) as replace,
                patch("rag_workflow.orchestrator.time.sleep"),
            ):
                with self.assertRaisesRegex(PermissionError, "latest snapshot remains"):
                    _replace_evidence_file(temporary_path, evidence_path)

            self.assertEqual(replace.call_count, 5)
            self.assertTrue(temporary_path.exists())
            self.assertEqual(
                evidence_path.read_text(encoding="utf-8"),
                "previous evidence",
            )

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