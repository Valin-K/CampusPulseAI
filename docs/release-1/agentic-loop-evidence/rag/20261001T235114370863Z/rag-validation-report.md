# RAG Validation Report

- Run ID: `20261001T235114370863Z`
- Recorded: `2026-10-01T23:51:14.371326+00:00`
- Result: **FAIL**
- Goal: Validate and improve the shared CampusPulseAI campus services solution.

## Workflow Stages

| Stage | Status | Evidence |
|---|---|---|
| OBSERVE | FAILED | 7 of 8 evidence checks passed; cosine similarity=0.8333; P@5=0.2; R@5=1.0; answer status=answered; negative-query status=answered. |
| IMPLEMENTATION | PASSED | Implementation model assessed the collected RAG execution evidence. |
| REVIEW | PASSED | Independent review completed against the same collected evidence. |

## OBSERVE: Collected Evidence

| Check | Result | Details |
|---|---|---|
| Start AI Gateway | PASS | Started and healthy at http://localhost:5269/health. |
| Start RAG server | PASS | Started and healthy at http://localhost:5267/health. |
| RAG health | PASS | Health contract validated. |
| RAG collections | PASS | Collection names and document/chunk counts validated. |
| RAG validation workspace | PASS | Selected unused collection coursemanagement for an isolated fixture. |
| RAG validation corpus refresh | PASS | Isolated corpus refreshed with 6 one-chunk documents. |
| RAG retrieval metrics | PASS | P@5=0.200; R@5=1.000; relevant chunks=1/1; cosine similarity=0.8333. |
| RAG retrieval and grounded answer | PASS | Answered with cosine similarity 0.8333; fixture citation agentic-loop-validation-ea66ee2bc7a448e485084934c468171c was present. |
| RAG negative-query abstention | FAIL / NOT RUN | Unanswerable query status='answered'; cosine similarity=0.5642; expected insufficient_context. |
| RAG validation corpus cleanup | PASS | Removed the isolated collection after confirming it contained only the fixture. |

```json
{
  "collection": "coursemanagement",
  "documentId": "agentic-loop-validation-ea66ee2bc7a448e485084934c468171c",
  "documentIds": [
    "agentic-loop-validation-ea66ee2bc7a448e485084934c468171c",
    "agentic-loop-validation-decoy-75a0a5b599c74a638b6817ccb1658123",
    "agentic-loop-validation-decoy-40d4873e8ee2468da905ab6b0e1b5a0d",
    "agentic-loop-validation-decoy-a1601a5ba9f94bc58f5f7c22a1250b49",
    "agentic-loop-validation-decoy-1b439741a0fa4b6f964c165cbf511867",
    "agentic-loop-validation-decoy-415f1f8cafb34364a8e9bb18bdde2854"
  ],
  "expectedRelevantChunks": 1,
  "marker": "CAMPUSPULSEVALIDATION2a6ad2692f5c",
  "fixtureText": "Validation marker CAMPUSPULSEVALIDATION2a6ad2692f5c: the north arch has a blue lantern.",
  "question": "What does marker CAMPUSPULSEVALIDATION2a6ad2692f5c identify?",
  "negativeQuestion": "Which municipal bylaw governs the 61b299c871f1 festival in Novanthera?",
  "ingestion": {
    "collection": "coursemanagement",
    "documents": 6,
    "chunks": 6,
    "embedder": "gemini-embedding-001"
  },
  "retrieval": {
    "collection": "coursemanagement",
    "question": "What does marker CAMPUSPULSEVALIDATION2a6ad2692f5c identify?",
    "topK": 5,
    "cosineSimilarity": 0.8333,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-ea66ee2bc7a448e485084934c468171c",
        "chunkIndex": 0,
        "score": 0.8333,
        "text": "Validation marker CAMPUSPULSEVALIDATION2a6ad2692f5c: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-decoy-40d4873e8ee2468da905ab6b0e1b5a0d",
        "chunkIndex": 0,
        "score": 0.5818,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-decoy-a1601a5ba9f94bc58f5f7c22a1250b49",
        "chunkIndex": 0,
        "score": 0.573,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-415f1f8cafb34364a8e9bb18bdde2854",
        "chunkIndex": 0,
        "score": 0.5707,
        "text": "Musical intervals compare frequencies of paired notes.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-1b439741a0fa4b6f964c165cbf511867",
        "chunkIndex": 0,
        "score": 0.5533,
        "text": "Root systems draw water from layers of surrounding soil.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "retrievedAtUtc": "2026-10-01T23:51:37.7335127+00:00"
  },
  "retrievalMetrics": {
    "k": 5,
    "retrievedCount": 5,
    "relevantRetrieved": 1,
    "relevantTotal": 1,
    "pAt5": 0.2,
    "rAt5": 1.0,
    "cosineSimilarity": 0.8333
  },
  "answer": {
    "collection": "coursemanagement",
    "question": "What does marker CAMPUSPULSEVALIDATION2a6ad2692f5c identify?",
    "status": "answered",
    "answer": "Based on the provided context, the validation marker CAMPUSPULSEVALIDATION2a6ad2692f5c identifies that the north arch has a blue lantern [1].",
    "confidence": 0.8333,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-ea66ee2bc7a448e485084934c468171c",
        "chunkIndex": 0,
        "score": 0.8333,
        "text": "Validation marker CAMPUSPULSEVALIDATION2a6ad2692f5c: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-decoy-40d4873e8ee2468da905ab6b0e1b5a0d",
        "chunkIndex": 0,
        "score": 0.5818,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-decoy-a1601a5ba9f94bc58f5f7c22a1250b49",
        "chunkIndex": 0,
        "score": 0.573,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-415f1f8cafb34364a8e9bb18bdde2854",
        "chunkIndex": 0,
        "score": 0.5707,
        "text": "Musical intervals compare frequencies of paired notes.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-1b439741a0fa4b6f964c165cbf511867",
        "chunkIndex": 0,
        "score": 0.5533,
        "text": "Root systems draw water from layers of surrounding soil.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "detail": null,
    "executedAtUtc": "2026-10-01T23:51:39.389046+00:00"
  },
  "negativeAnswer": {
    "collection": "coursemanagement",
    "question": "Which municipal bylaw governs the 61b299c871f1 festival in Novanthera?",
    "status": "answered",
    "answer": "I do not have enough information to answer this question.",
    "confidence": 0.5642,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-ea66ee2bc7a448e485084934c468171c",
        "chunkIndex": 0,
        "score": 0.5642,
        "text": "Validation marker CAMPUSPULSEVALIDATION2a6ad2692f5c: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-decoy-40d4873e8ee2468da905ab6b0e1b5a0d",
        "chunkIndex": 0,
        "score": 0.5398,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-decoy-a1601a5ba9f94bc58f5f7c22a1250b49",
        "chunkIndex": 0,
        "score": 0.5256,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-415f1f8cafb34364a8e9bb18bdde2854",
        "chunkIndex": 0,
        "score": 0.5182,
        "text": "Musical intervals compare frequencies of paired notes.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-1b439741a0fa4b6f964c165cbf511867",
        "chunkIndex": 0,
        "score": 0.5124,
        "text": "Root systems draw water from layers of surrounding soil.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "detail": null,
    "executedAtUtc": "2026-10-01T23:51:40.7934124+00:00"
  },
  "cleanup": {
    "collection": "coursemanagement",
    "deleted": true
  }
}
```

## IMPLEMENTATION: Assessment

Isolated refresh, top-5 retrieval (P@5=0.2, R@5=1.0), grounded citation, and cleanup all passed. Negative-query abstention failed: status returned "answered" instead of "insufficient_context" despite low cosine similarity (0.5642).

## REVIEW: Independent Review

Risk: Negative-query abstention failed—status "answered" instead of "insufficient_context" at cosine 0.5642; threshold miscalibrated.
Correction: Set abstention threshold between 0.5642 and 0.8333 (e.g., 0.70).
Retest: Re-run negative query; expect status "insufficient_context."

Strengths: Isolated corpus, cleanup, grounded citation, correct P@5/R@5.
Risks: Single relevant chunk limits benchmark; threshold gap unaddressed.
Recommendations: Add multi-relevance fixtures; tune abstention threshold.

## IMPROVE: Recorded Follow-Up

Risk: Negative-query abstention failed—status "answered" instead of "insufficient_context" at cosine 0.5642; threshold miscalibrated.
Correction: Set abstention threshold between 0.5642 and 0.8333 (e.g., 0.70).
Retest: Re-run negative query; expect status "insufficient_context."

Strengths: Isolated corpus, cleanup, grounded citation, correct P@5/R@5.
Risks: Single relevant chunk limits benchmark; threshold gap unaddressed.
Recommendations: Add multi-relevance fixtures; tune abstention threshold.

- Status: recommendations_recorded
- Source changes applied automatically: **no**
- Next step: Apply an accepted correction and rerun validation.

## Human Review

- Decision: **pending**
- Notes: Pending human review.

## Privacy

Only the synthetic validation fixture and its response are supplied to the models. Source code and API keys are not recorded or sent.
