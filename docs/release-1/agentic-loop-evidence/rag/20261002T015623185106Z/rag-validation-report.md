# RAG Validation Report

- Run ID: `20261002T015623185106Z`
- Recorded: `2026-10-02T01:56:23.185757+00:00`
- Result: **PASS**
- Goal: Validate and improve the shared CampusPulseAI campus services solution.

## Workflow Stages

| Stage | Status | Evidence |
|---|---|---|
| OBSERVE | PASSED | 8 of 8 evidence checks passed; cosine similarity=0.8469; P@5=0.6; R@5=1.0; answer status=answered; negative-query status=insufficient_context. |
| IMPLEMENTATION | PASSED | Implementation model assessed the collected RAG execution evidence. |
| REVIEW | PASSED | Independent review completed against the same collected evidence. |

## OBSERVE: Collected Evidence

| Check | Result | Details |
|---|---|---|
| Start AI Gateway | PASS | Already healthy at http://localhost:5269/health. |
| Start RAG server | PASS | Already healthy at http://localhost:5267/health. |
| RAG health | PASS | Health contract validated. |
| RAG collections | PASS | Collection names and document/chunk counts validated. |
| RAG validation workspace | PASS | Selected unused collection coursemanagement for an isolated fixture. |
| RAG validation corpus refresh | PASS | Isolated corpus refreshed with 8 one-chunk documents. |
| RAG retrieval metrics | PASS | P@5=0.600; R@5=1.000; relevant chunks=3/3; cosine similarity=0.8469. |
| RAG retrieval and grounded answer | PASS | Answered with cosine similarity 0.8469; fixture citation agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33 was present. |
| RAG negative-query abstention | PASS | Unanswerable query status='insufficient_context'; cosine similarity=0.5772; expected insufficient_context. |
| RAG validation corpus cleanup | PASS | Removed the isolated collection after confirming it contained only the fixture. |

```json
{
  "collection": "coursemanagement",
  "documentId": "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
  "documentIds": [
    "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
    "agentic-loop-validation-ce64d81b33684f01a04134637601127a",
    "agentic-loop-validation-965ac8d3e92d49c5b687c3bfe81a9895",
    "agentic-loop-validation-decoy-a2449c736b034903a9acf7df18c2d97c",
    "agentic-loop-validation-decoy-f43b416233c64057a371b0b64dd2e889",
    "agentic-loop-validation-decoy-9b1cf1a408a449cb95b5d24c9e0f70af",
    "agentic-loop-validation-decoy-e6a868b5a06c457bb220e5e71d95255c",
    "agentic-loop-validation-decoy-fe4a8f9f0e7144e3a575743b1ca5d8e5"
  ],
  "relevantDocumentIds": [
    "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
    "agentic-loop-validation-ce64d81b33684f01a04134637601127a",
    "agentic-loop-validation-965ac8d3e92d49c5b687c3bfe81a9895"
  ],
  "relevantFixtures": [
    {
      "documentId": "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
      "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch has a blue lantern."
    },
    {
      "documentId": "agentic-loop-validation-ce64d81b33684f01a04134637601127a",
      "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch is beside the eastern fountain."
    },
    {
      "documentId": "agentic-loop-validation-965ac8d3e92d49c5b687c3bfe81a9895",
      "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch marks the start of the spring orientation route."
    }
  ],
  "expectedRelevantChunks": 3,
  "marker": "CAMPUSPULSEVALIDATION42cb73ce3fe1",
  "fixtureText": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch has a blue lantern.",
  "question": "What facts are recorded for marker CAMPUSPULSEVALIDATION42cb73ce3fe1?",
  "negativeQuestion": "Which municipal bylaw governs the 56e87f36413e festival in Novanthera?",
  "ingestion": {
    "collection": "coursemanagement",
    "documents": 8,
    "chunks": 8,
    "embedder": "gemini-embedding-001"
  },
  "retrieval": {
    "collection": "coursemanagement",
    "question": "What facts are recorded for marker CAMPUSPULSEVALIDATION42cb73ce3fe1?",
    "topK": 5,
    "cosineSimilarity": 0.8469,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-ce64d81b33684f01a04134637601127a",
        "chunkIndex": 0,
        "score": 0.8469,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch is beside the eastern fountain.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-965ac8d3e92d49c5b687c3bfe81a9895",
        "chunkIndex": 0,
        "score": 0.8395,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch marks the start of the spring orientation route.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
        "chunkIndex": 0,
        "score": 0.8371,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-9b1cf1a408a449cb95b5d24c9e0f70af",
        "chunkIndex": 0,
        "score": 0.5773,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-f43b416233c64057a371b0b64dd2e889",
        "chunkIndex": 0,
        "score": 0.5724,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "retrievedAtUtc": "2026-10-02T01:56:25.2039419+00:00"
  },
  "retrievalMetrics": {
    "k": 5,
    "retrievedCount": 5,
    "relevantRetrieved": 3,
    "relevantTotal": 3,
    "pAt5": 0.6,
    "rAt5": 1.0,
    "cosineSimilarity": 0.8469
  },
  "answer": {
    "collection": "coursemanagement",
    "question": "What facts are recorded for marker CAMPUSPULSEVALIDATION42cb73ce3fe1?",
    "status": "answered",
    "answer": "Based on the provided context, the facts recorded for marker CAMPUSPULSEVALIDATION42cb73ce3fe1 are:\n- The north arch is beside the eastern fountain [1].\n- The north arch marks the start of the spring orientation route [2].\n- The north arch has a blue lantern [3].",
    "confidence": 0.8469,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-ce64d81b33684f01a04134637601127a",
        "chunkIndex": 0,
        "score": 0.8469,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch is beside the eastern fountain.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-965ac8d3e92d49c5b687c3bfe81a9895",
        "chunkIndex": 0,
        "score": 0.8395,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch marks the start of the spring orientation route.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
        "chunkIndex": 0,
        "score": 0.8371,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-9b1cf1a408a449cb95b5d24c9e0f70af",
        "chunkIndex": 0,
        "score": 0.5773,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-f43b416233c64057a371b0b64dd2e889",
        "chunkIndex": 0,
        "score": 0.5724,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "detail": null,
    "executedAtUtc": "2026-10-02T01:56:26.9389445+00:00"
  },
  "negativeAnswer": {
    "collection": "coursemanagement",
    "question": "Which municipal bylaw governs the 56e87f36413e festival in Novanthera?",
    "status": "insufficient_context",
    "answer": "The knowledge base does not contain enough information to answer this question.",
    "confidence": 0.5772,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-f7bad84956a34e1784a6f14004b98f33",
        "chunkIndex": 0,
        "score": 0.5772,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-965ac8d3e92d49c5b687c3bfe81a9895",
        "chunkIndex": 0,
        "score": 0.5691,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch marks the start of the spring orientation route.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-ce64d81b33684f01a04134637601127a",
        "chunkIndex": 0,
        "score": 0.5671,
        "text": "Validation marker CAMPUSPULSEVALIDATION42cb73ce3fe1: the north arch is beside the eastern fountain.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-f43b416233c64057a371b0b64dd2e889",
        "chunkIndex": 0,
        "score": 0.5561,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-9b1cf1a408a449cb95b5d24c9e0f70af",
        "chunkIndex": 0,
        "score": 0.5334,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "detail": "Top score 0.5772 is below the threshold 0.7.",
    "executedAtUtc": "2026-10-02T01:56:27.3844332+00:00"
  },
  "cleanup": {
    "collection": "coursemanagement",
    "deleted": true
  }
}
```

## IMPLEMENTATION: Assessment

All six concepts validated: isolated refresh, top-5 retrieval, P@5=0.6/R@5=1.0, cited grounded answer, threshold-based abstention, and fixture cleanup. Cosine similarity serves as retrieval score only. No gaps observed.

## REVIEW: Independent Review

Risk:
P@5=0.6 reflects 2/5 decoys, not a retrieval defect; single-query metrics lack statistical power.

Correction:
Cosine similarity is a retrieval score, not a calibrated confidence; threshold 0.7 is unvalidated.

Retest:
Add multi-query benchmark and threshold sensitivity analysis.

Strengths:
Isolated fixture, clean cleanup, grounded citations, correct abstention.

Risks:
Single-query metrics; uncalibrated threshold; no chunking/embedding strategy documented.

Recommendations:
Expand benchmark; validate threshold; document chunking and embedding choices.

## IMPROVE: Recorded Follow-Up

Risk:
P@5=0.6 reflects 2/5 decoys, not a retrieval defect; single-query metrics lack statistical power.

Correction:
Cosine similarity is a retrieval score, not a calibrated confidence; threshold 0.7 is unvalidated.

Retest:
Add multi-query benchmark and threshold sensitivity analysis.

Strengths:
Isolated fixture, clean cleanup, grounded citations, correct abstention.

Risks:
Single-query metrics; uncalibrated threshold; no chunking/embedding strategy documented.

Recommendations:
Expand benchmark; validate threshold; document chunking and embedding choices.

- Status: recommendations_recorded
- Source changes applied automatically: **no**
- Next step: Apply an accepted correction and rerun validation.

## Human Review

- Decision: **pending**
- Notes: Pending human review.

## Privacy

Only the synthetic validation fixture and its response are supplied to the models. Source code and API keys are not recorded or sent.
