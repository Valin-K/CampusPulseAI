# Architecture Documentation

| Document | Purpose |
|---|---|
| [system-architecture.md](system-architecture.md) | Platform, layering, container diagram, integration map, persistence, quality gates and known gaps. |
| [decisions.md](decisions.md) | Architecture decision records for the choices that shape the solution. |

Domain-level design for individual services lives with its release:

- [Financial Aid & Billing (student-4)](../release-0/student-4-financial-aid-billing.md)

## Quick reference

- **SDK:** .NET 10.0.400 · **Packages:** pinned centrally in `Directory.Packages.props`
- **Pattern:** frontend (Blazor WASM) → backend (minimal API) → database service (EF Core + SQLite)
- **Orchestration:** Docker Compose, shell at <http://localhost:8080>
- **Discovery:** `Services__*` environment variables resolving to Compose service names
- **Hub service:** Academic Records — every other domain reads student identity from it
