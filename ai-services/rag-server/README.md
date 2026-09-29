# Shared RAG server

One retrieval-augmented generation service shared by all five features. It runs on the
host, not in Docker Compose, and each backend container reaches it at
`http://host.docker.internal:5267` through the `Services__RagServer` environment variable.

Each feature owns a **collection**. Documents you put into your collection are only ever
retrieved by questions asked against your collection. Nothing is shared between features.

| Feature | Collection |
|---|---|
| Student 1 Academic Records | `academicrecords` |
| Student 2 Course Management | `coursemanagement` |
| Student 3 Campus Housing | `campushousing` |
| Student 4 Financial Aid & Billing | `financialaidbilling` |
| Student 5 Event Scheduling | `eventscheduling` |

## Running it

```powershell
# from the repository root
dotnet run --project ai-services\rag-server\RagServer\RagServer.csproj
```

Start it before `docker compose up`. RAG uses the shared AI gateway at
`Services:AiGateway` (environment variable `Services__AiGateway`, default
`http://localhost:5269`). All provider credentials and model configuration belong
to the gateway, not this server. Compose starts the gateway; alternatively, run it
in another terminal before ingesting or asking questions:

```powershell
dotnet run --project shared\backend\CampusPulse.Shared.Ai.Gateway\CampusPulse.Shared.Ai.Gateway.csproj --no-launch-profile --urls http://localhost:5269
```

If the gateway explicitly reports generation unavailable, RAG uses local
keyword-based embeddings and returns retrieved context with
`status: generation_unavailable` instead of a generated answer. CI uses this
keyless mode. Gateway connection failures never enable fallback: embedding
requests return `502`, and generation errors return `generation_failed`.

The first successful gateway status response pins the embedding mode, model and
dimensions for this RAG process. Failed initialization can be retried. Subsequent
operations check the configuration again and validate returned embedding models
and dimensions. Changing gateway configuration requires restarting RAG and
re-ingesting documents; incompatible vectors are never silently mixed.

`/health` remains available before the gateway starts. Until the first AI
operation, its embedder is `uninitialized` and `generationAvailable` is `null`.
After initialization these fields describe the pinned configuration, not gateway
connectivity.

Data is held in memory and is lost when the process stops. Seed your collection from your
backend on startup or behind an admin endpoint.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Service status, active embedder, whether generation is available |
| GET | `/api/rag/collections` | Document and chunk counts per collection |
| POST | `/api/rag/{collection}/documents` | Ingest documents. Same `id` replaces the earlier version |
| POST | `/api/rag/{collection}/answer` | Ask a question against one collection |
| DELETE | `/api/rag/{collection}` | Remove everything in a collection |

Unknown collection names return `404` with the allowed list.

### Ingest

```json
POST /api/rag/coursemanagement/documents
{
  "documents": [
    { "id": "COMP1001", "text": "...", "metadata": { "courseCode": "COMP1001" } }
  ]
}
```

Response: `{ "collection", "documents", "chunks", "embedder" }`. The server splits each
document into overlapping chunks, embeds them and stores them. `metadata` is returned
unchanged with every source, so put whatever your UI needs for a citation in it.

### Answer

```json
POST /api/rag/coursemanagement/answer
{
  "question": "What are the prerequisites for COMP2002?",
  "topK": 4,
  "instructions": "You are the Course Management assistant. Refer to courses by code.",
  "minimumConfidence": 0.55
}
```

`topK`, `instructions` and `minimumConfidence` are optional.

Response:

```json
{
  "collection": "coursemanagement",
  "question": "...",
  "status": "answered",
  "answer": "Students must have passed COMP1001 [2].",
  "confidence": 0.81,
  "sources": [
    { "rank": 1, "documentId": "COMP2002", "chunkIndex": 0, "score": 0.81, "text": "...", "metadata": { "courseCode": "COMP2002" } }
  ],
  "embedder": "gemini-embedding-001",
  "detail": null,
  "executedAtUtc": "..."
}
```

`status` is one of:

| Status | Meaning | `answer` |
|---|---|---|
| `answered` | Retrieved context passed the confidence threshold and Gemini produced a response | generated text with `[n]` citations into `sources` |
| `insufficient_context` | No chunks matched, or the best match scored below `minimumConfidence` | fixed "not enough information" message |
| `generation_unavailable` | Gateway explicitly reports generation unavailable | `null`, `sources` still populated |
| `generation_failed` | Gateway errored, changed configuration, or returned nothing | `null`, see `detail` |

`confidence` is the cosine similarity of the best-matching chunk. `sources` are always
returned so your backend can show citations or apply its own policy.

## Integrating from your backend

1. Register a named client using the URL already injected by Compose:
   `builder.Configuration["Services:RagServer"] ?? "http://localhost:5267"`.
2. Seed your collection with your own domain text. Only you know what your feature should
   be able to answer.
3. Add an endpoint such as `POST /api/courses/ask` that forwards the user's question to
   `/api/rag/{your collection}/answer` and shapes the result for your UI.
4. Show the `answer` and the `sources` in your frontend.
