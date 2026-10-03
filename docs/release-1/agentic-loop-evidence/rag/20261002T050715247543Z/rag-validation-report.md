# RAG Validation Report

- Run ID: `20261002T050715247543Z`
- Recorded: `2026-10-02T05:07:15.250114+00:00`
- Result: **PASS**
- Goal: Validate the RAG retrieval, answer grounding, and abstention behavior.

## Workflow Stages

| Stage | Status | Evidence |
|---|---|---|
| OBSERVE | PASSED | 8 of 8 evidence checks passed; cosine similarity=0.8454; P@5=0.6; R@5=1.0; answer status=answered; negative-query status=insufficient_context. |
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
| RAG retrieval metrics | PASS | P@5=0.600; R@5=1.000; relevant chunks=3/3; cosine similarity=0.8454. |
| RAG retrieval and grounded answer | PASS | Answered with cosine similarity 0.8454; fixture citation agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0 was present. |
| RAG negative-query abstention | PASS | Unanswerable query status='insufficient_context'; cosine similarity=0.5644; expected insufficient_context. |
| RAG validation corpus cleanup | PASS | Removed the isolated collection after confirming it contained only the fixture. |

```json
{
  "collection": "coursemanagement",
  "documentId": "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
  "documentIds": [
    "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
    "agentic-loop-validation-45080791c652405d86e73fc9d3bb15d3",
    "agentic-loop-validation-c3baf6499d5d42e7ad880d4eed25c5dd",
    "agentic-loop-validation-decoy-32029bac60b34aa596e0f4333785560b",
    "agentic-loop-validation-decoy-e478938296bc49afb216d6404379bbd0",
    "agentic-loop-validation-decoy-0035e759cb1a464b89b61c816e9ae2a4",
    "agentic-loop-validation-decoy-3345027d87eb40cba7bca162f065c409",
    "agentic-loop-validation-decoy-72c75004b05e41e58685663ec0e1faf7"
  ],
  "relevantDocumentIds": [
    "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
    "agentic-loop-validation-45080791c652405d86e73fc9d3bb15d3",
    "agentic-loop-validation-c3baf6499d5d42e7ad880d4eed25c5dd"
  ],
  "relevantFixtures": [
    {
      "documentId": "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
      "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch has a blue lantern."
    },
    {
      "documentId": "agentic-loop-validation-45080791c652405d86e73fc9d3bb15d3",
      "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch is beside the eastern fountain."
    },
    {
      "documentId": "agentic-loop-validation-c3baf6499d5d42e7ad880d4eed25c5dd",
      "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch marks the start of the spring orientation route."
    }
  ],
  "expectedRelevantChunks": 3,
  "marker": "CAMPUSPULSEVALIDATION0d227158e22b",
  "fixtureText": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch has a blue lantern.",
  "question": "What facts are recorded for marker CAMPUSPULSEVALIDATION0d227158e22b?",
  "negativeQuestion": "Which municipal bylaw governs the 402ae0bef20e festival in Novanthera?",
  "ingestion": {
    "collection": "coursemanagement",
    "documents": 8,
    "chunks": 8,
    "embedder": "gemini-embedding-001"
  },
  "retrieval": {
    "collection": "coursemanagement",
    "question": "What facts are recorded for marker CAMPUSPULSEVALIDATION0d227158e22b?",
    "topK": 5,
    "cosineSimilarity": 0.8454,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-45080791c652405d86e73fc9d3bb15d3",
        "chunkIndex": 0,
        "score": 0.8454,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch is beside the eastern fountain.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
        "chunkIndex": 0,
        "score": 0.8413,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-c3baf6499d5d42e7ad880d4eed25c5dd",
        "chunkIndex": 0,
        "score": 0.8381,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch marks the start of the spring orientation route.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-0035e759cb1a464b89b61c816e9ae2a4",
        "chunkIndex": 0,
        "score": 0.5869,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-e478938296bc49afb216d6404379bbd0",
        "chunkIndex": 0,
        "score": 0.583,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "retrievedAtUtc": "2026-10-02T05:07:17.5059489+00:00"
  },
  "retrievalMetrics": {
    "k": 5,
    "retrievedCount": 5,
    "relevantRetrieved": 3,
    "relevantTotal": 3,
    "pAt5": 0.6,
    "rAt5": 1.0,
    "cosineSimilarity": 0.8454
  },
  "answer": {
    "collection": "coursemanagement",
    "question": "What facts are recorded for marker CAMPUSPULSEVALIDATION0d227158e22b?",
    "status": "answered",
    "answer": "Based on the provided context, the facts recorded for marker CAMPUSPULSEVALIDATION0d227158e22b are:\n- The north arch is beside the eastern fountain [1].\n- The north arch has a blue lantern [2].\n- The north arch marks the start of the spring orientation route [3].",
    "confidence": 0.8454,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-45080791c652405d86e73fc9d3bb15d3",
        "chunkIndex": 0,
        "score": 0.8454,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch is beside the eastern fountain.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
        "chunkIndex": 0,
        "score": 0.8413,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-c3baf6499d5d42e7ad880d4eed25c5dd",
        "chunkIndex": 0,
        "score": 0.8381,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch marks the start of the spring orientation route.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-0035e759cb1a464b89b61c816e9ae2a4",
        "chunkIndex": 0,
        "score": 0.5869,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-e478938296bc49afb216d6404379bbd0",
        "chunkIndex": 0,
        "score": 0.583,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "detail": null,
    "executedAtUtc": "2026-10-02T05:07:19.1255105+00:00"
  },
  "negativeAnswer": {
    "collection": "coursemanagement",
    "question": "Which municipal bylaw governs the 402ae0bef20e festival in Novanthera?",
    "status": "insufficient_context",
    "answer": "The knowledge base does not contain enough information to answer this question.",
    "confidence": 0.5644,
    "sources": [
      {
        "rank": 1,
        "documentId": "agentic-loop-validation-f45f3660eb794261a06606ee6a3536a0",
        "chunkIndex": 0,
        "score": 0.5644,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch has a blue lantern.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 2,
        "documentId": "agentic-loop-validation-c3baf6499d5d42e7ad880d4eed25c5dd",
        "chunkIndex": 0,
        "score": 0.5596,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch marks the start of the spring orientation route.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 3,
        "documentId": "agentic-loop-validation-45080791c652405d86e73fc9d3bb15d3",
        "chunkIndex": 0,
        "score": 0.558,
        "text": "Validation marker CAMPUSPULSEVALIDATION0d227158e22b: the north arch is beside the eastern fountain.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "relevant"
        }
      },
      {
        "rank": 4,
        "documentId": "agentic-loop-validation-decoy-e478938296bc49afb216d6404379bbd0",
        "chunkIndex": 0,
        "score": 0.5366,
        "text": "Kepler cycles describe repeated paths of distant planets.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      },
      {
        "rank": 5,
        "documentId": "agentic-loop-validation-decoy-0035e759cb1a464b89b61c816e9ae2a4",
        "chunkIndex": 0,
        "score": 0.5237,
        "text": "Archival ink remains legible when stored away from sunlight.",
        "metadata": {
          "source": "agentic-loop-validation",
          "relevance": "decoy"
        }
      }
    ],
    "embedder": "gemini-embedding-001",
    "detail": "Top score 0.5644 is below the threshold 0.7.",
    "executedAtUtc": "2026-10-02T05:07:19.6644559+00:00"
  },
  "cleanup": {
    "collection": "coursemanagement",
    "deleted": true
  }
}
```

## IMPLEMENTATION: Assessment

All checks passed. Isolated corpus refresh, top-5 retrieval (P@5=0.6, R@5=1.0), grounded citations, negative-query abstention via threshold, and fixture cleanup are all evidenced. Cosine similarity correctly treated as retrieval score, not confidence.

## REVIEW: Independent Review

Risk: Answer JSON labels cosine similarity as "confidence," contradicting the claim it's "not confidence."
Correction: Rename to "retrievalScore" or document proxy semantics.
Retest: Validate answer schema field naming against documented semantics.

Strengths: Isolated fixture with decoys, P@5/R@5 metrics, threshold-based abstention, cleanup verified.
Risks: Single-query benchmark; "confidence" field mislabels retrieval score; no multi-turn or edge-case coverage.
Recommendations: Expand benchmark to multi-query; rename confidence field; add threshold sensitivity analysis.

## IMPROVE: Recorded Follow-Up

Risk: Answer JSON labels cosine similarity as "confidence," contradicting the claim it's "not confidence."
Correction: Rename to "retrievalScore" or document proxy semantics.
Retest: Validate answer schema field naming against documented semantics.

Strengths: Isolated fixture with decoys, P@5/R@5 metrics, threshold-based abstention, cleanup verified.
Risks: Single-query benchmark; "confidence" field mislabels retrieval score; no multi-turn or edge-case coverage.
Recommendations: Expand benchmark to multi-query; rename confidence field; add threshold sensitivity analysis.

- Status: recommendations_recorded
- Source changes applied automatically: **no**
- Next step: Apply an accepted correction and rerun validation.

## Human Review

- Decision: **pending**
- Notes: Pending human review.

## Privacy

Only the synthetic validation fixture and its response are supplied to the models. Source code and API keys are not recorded or sent.
