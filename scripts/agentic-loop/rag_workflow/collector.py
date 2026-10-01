from __future__ import annotations

import math
import time
import uuid
from typing import Any, Callable
from urllib.parse import quote

import requests


RequestFunc = Callable[..., Any]


class RagCollector:
    """Collect bounded RAG evidence using the CampusPulseAI HTTP contract."""

    def __init__(
        self,
        server_url: str,
        request_func: RequestFunc | None = None,
        timeout: int = 30,
    ) -> None:
        self.server_url = server_url.rstrip("/")
        self.request_func = request_func or requests.request
        self.timeout = timeout
        self.checks: list[dict[str, Any]] = []
        self.health_evidence: dict[str, Any] | None = None
        self.collection_evidence: list[dict[str, Any]] | None = None
        self.validation_evidence: dict[str, Any] = {}

    def _request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
    ) -> Any:
        response = self.request_func(
            method,
            f"{self.server_url}{path}",
            json=payload,
            timeout=self.timeout,
        )
        if not 200 <= response.status_code < 300:
            raise RuntimeError(
                f"HTTP {response.status_code}: {response.text[:500]}"
            )
        try:
            return response.json()
        except ValueError as exc:
            raise RuntimeError("Expected a JSON response.") from exc

    def _record_check(
        self,
        name: str,
        passed: bool,
        output: str,
        started: float,
    ) -> None:
        self.checks.append({
            "name": name,
            "passed": passed,
            "skipped": False,
            "exitCode": 0 if passed else 1,
            "durationSeconds": round(time.perf_counter() - started, 2),
            "output": output,
        })

    def _result(self) -> dict[str, Any]:
        return {
            "passed": bool(self.checks) and all(
                check["passed"] and not check["skipped"]
                for check in self.checks
            ),
            "healthEvidence": self.health_evidence,
            "collectionEvidence": self.collection_evidence,
            "validationEvidence": self.validation_evidence,
            "checks": self.checks,
        }

    def collect_all(self) -> dict[str, Any]:
        self.checks = []
        self.health_evidence = None
        self.collection_evidence = None
        self.validation_evidence = {}

        started = time.perf_counter()
        try:
            health = self._request("GET", "/health")
        except Exception as exc:
            self._record_check("RAG health", False, str(exc), started)
            return self._result()

        self.health_evidence = health if isinstance(health, dict) else None
        allowed_collections = (
            health.get("allowedCollections") if isinstance(health, dict) else None
        )
        health_valid = (
            isinstance(health, dict)
            and health.get("status") == "healthy"
            and health.get("service") == "rag"
            and isinstance(allowed_collections, list)
            and all(isinstance(name, str) and name for name in allowed_collections)
        )
        self._record_check(
            "RAG health",
            health_valid,
            "Health contract validated."
            if health_valid
            else "RAG health response is missing healthy status, service, or collection policy.",
            started,
        )
        if not health_valid:
            return self._result()

        started = time.perf_counter()
        try:
            collections = self._request("GET", "/api/rag/collections")
        except Exception as exc:
            self._record_check("RAG collections", False, str(exc), started)
            return self._result()

        collections_valid = isinstance(collections, list) and all(
            isinstance(collection, dict)
            and isinstance(collection.get("name"), str)
            and isinstance(collection.get("documents"), int)
            and isinstance(collection.get("chunks"), int)
            for collection in collections
        )
        self.collection_evidence = collections if collections_valid else None
        self._record_check(
            "RAG collections",
            collections_valid,
            "Collection names and document/chunk counts validated."
            if collections_valid
            else "RAG collections response must contain names and document/chunk counts.",
            started,
        )
        if not collections_valid:
            return self._result()

        existing_collections = {
            item["name"].casefold() for item in collections
        }
        if allowed_collections:
            preferred_collection = next(
                (
                    name for name in allowed_collections
                    if name.casefold() == "coursemanagement"
                ),
                None,
            )
            candidates = (
                ([preferred_collection] if preferred_collection else [])
                + [name for name in allowed_collections if name != preferred_collection]
            )
        else:
            candidates = [f"agenticvalidation-{uuid.uuid4().hex[:8]}"]

        started = time.perf_counter()
        collection = next(
            (
                name for name in candidates
                if name.casefold() not in existing_collections
            ),
            None,
        )
        if collection is None:
            self.validation_evidence = {}
            self._record_check(
                "RAG validation workspace",
                False,
                "No unused allowed collection is available; existing collections were left untouched.",
                started,
            )
            return self._result()
        self._record_check(
            "RAG validation workspace",
            True,
            f"Selected unused collection {collection} for an isolated fixture.",
            started,
        )

        fixture_id = f"agentic-loop-validation-{uuid.uuid4().hex}"
        marker = f"CAMPUSPULSEVALIDATION{uuid.uuid4().hex[:12]}"
        fixture_text = (
            f"CampusPulseAI RAG validation fixture marker {marker}. "
            "The marker confirms grounded citation retrieval."
        )
        question = f"What does marker {marker} confirm?"
        self.validation_evidence = {
            "collection": collection,
            "documentId": fixture_id,
            "marker": marker,
            "fixtureText": fixture_text,
            "question": question,
        }

        fixture_attempted = True
        ingest_succeeded = False
        started = time.perf_counter()
        try:
            ingestion = self._request(
                "POST",
                f"/api/rag/{quote(collection, safe='')}/documents",
                {
                    "documents": [{
                        "id": fixture_id,
                        "text": fixture_text,
                        "metadata": {"source": "agentic-loop-validation"},
                    }]
                },
            )
            ingest_succeeded = True
            self.validation_evidence["ingestion"] = ingestion
            ingestion_valid = (
                isinstance(ingestion, dict)
                and isinstance(ingestion.get("collection"), str)
                and ingestion["collection"].casefold() == collection.casefold()
                and ingestion.get("documents") == 1
                and isinstance(ingestion.get("chunks"), int)
                and ingestion["chunks"] > 0
            )
            self._record_check(
                "RAG validation corpus seed",
                ingestion_valid,
                "Unique validation fixture indexed."
                if ingestion_valid
                else "RAG did not confirm indexing the unique validation fixture.",
                started,
            )
        except Exception as exc:
            ingestion_valid = False
            self._record_check("RAG validation corpus seed", False, str(exc), started)

        started = time.perf_counter()
        if ingestion_valid:
            try:
                answer = self._request(
                    "POST",
                    f"/api/rag/{quote(collection, safe='')}/answer",
                    {"question": question, "topK": 5},
                )
                self.validation_evidence["answer"] = answer
                sources = answer.get("sources") if isinstance(answer, dict) else None
                citations_valid = isinstance(sources, list) and any(
                    isinstance(source, dict)
                    and source.get("documentId") == fixture_id
                    and isinstance(source.get("rank"), int)
                    and not isinstance(source.get("rank"), bool)
                    and source["rank"] > 0
                    and isinstance(source.get("chunkIndex"), int)
                    and not isinstance(source.get("chunkIndex"), bool)
                    and source["chunkIndex"] >= 0
                    and isinstance(source.get("score"), (int, float))
                    and not isinstance(source.get("score"), bool)
                    and math.isfinite(source["score"])
                    and -1 <= source["score"] <= 1
                    and isinstance(source.get("text"), str)
                    and marker in source["text"]
                    for source in sources
                )
                confidence = answer.get("confidence") if isinstance(answer, dict) else None
                answer_valid = (
                    isinstance(answer, dict)
                    and answer.get("status") == "answered"
                    and isinstance(answer.get("answer"), str)
                    and bool(answer["answer"].strip())
                    and isinstance(confidence, (int, float))
                    and not isinstance(confidence, bool)
                    and math.isfinite(confidence)
                    and -1 <= confidence <= 1
                    and citations_valid
                )
                if answer_valid:
                    output = (
                        f"Answered with confidence {confidence}; "
                        f"fixture citation {fixture_id} was present."
                    )
                elif isinstance(answer, dict):
                    output = (
                        f"Expected an answered response with finite confidence and "
                        f"a citation to fixture {fixture_id}; received status="
                        f"{answer.get('status')!r}, citations="
                        f"{len(sources) if isinstance(sources, list) else 'invalid'}."
                    )
                else:
                    output = "RAG answer response must be a JSON object."
                self._record_check(
                    "RAG retrieval and grounded answer",
                    answer_valid,
                    output,
                    started,
                )
            except Exception as exc:
                self._record_check(
                    "RAG retrieval and grounded answer", False, str(exc), started
                )
        else:
            self._record_check(
                "RAG retrieval and grounded answer",
                False,
                "Skipped because the validation fixture was not indexed.",
                started,
            )

        if fixture_attempted:
            started = time.perf_counter()
            try:
                current_collections = self._request(
                    "GET", "/api/rag/collections"
                )
                current_collections_valid = isinstance(current_collections, list) and all(
                    isinstance(item, dict)
                    and isinstance(item.get("name"), str)
                    and isinstance(item.get("documents"), int)
                    and isinstance(item.get("chunks"), int)
                    for item in current_collections
                )
                if not current_collections_valid:
                    raise RuntimeError("Could not verify the validation collection before cleanup.")

                current_collection = next(
                    (
                        item for item in current_collections
                        if item["name"].casefold() == collection.casefold()
                    ),
                    None,
                )
                ingestion = self.validation_evidence.get("ingestion")
                expected_chunks = (
                    ingestion.get("chunks") if isinstance(ingestion, dict) else None
                )
                if not ingest_succeeded:
                    cleanup_valid = current_collection is None
                    self.validation_evidence["cleanup"] = {
                        "deleted": False,
                        "collectionPresent": current_collection is not None,
                    }
                    cleanup_output = (
                        "Seed failed and the target collection stayed absent; no cleanup was needed."
                        if cleanup_valid
                        else "Seed was not confirmed and the collection appeared; left it untouched."
                    )
                elif (
                    current_collection is not None
                    and current_collection["documents"] == 1
                    and current_collection["chunks"] == expected_chunks
                ):
                    cleanup = self._request(
                        "DELETE",
                        f"/api/rag/{quote(collection, safe='')}",
                    )
                    self.validation_evidence["cleanup"] = cleanup
                    cleanup_valid = (
                        isinstance(cleanup, dict)
                        and isinstance(cleanup.get("collection"), str)
                        and cleanup["collection"].casefold() == collection.casefold()
                        and cleanup.get("deleted") is True
                    )
                    cleanup_output = (
                        "Removed the isolated collection after confirming it contained only the fixture."
                        if cleanup_valid
                        else "Could not confirm removal of the isolated validation collection."
                    )
                else:
                    cleanup_valid = False
                    self.validation_evidence["cleanup"] = {
                        "deleted": False,
                        "reason": "Collection was not fixture-only; left untouched.",
                    }
                    cleanup_output = "Collection was no longer fixture-only; left it untouched."
                self._record_check(
                    "RAG validation corpus cleanup",
                    cleanup_valid,
                    cleanup_output,
                    started,
                )
            except Exception as exc:
                self._record_check(
                    "RAG validation corpus cleanup", False, str(exc), started
                )

        return self._result()