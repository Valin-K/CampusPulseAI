# Release 0 — Documentation Index

Release 0 delivers the integrated CampusPulseAI solution: five student microservices, a
shared Blazor component library, the AI services, and the shared agentic validation loop.

## Contents

| Document | Purpose |
|---|---|
| [agentic-loop-technical-report-section.md](agentic-loop-technical-report-section.md) | The Plan → Act → Observe → Adapt loop, model routing and configuration. |
| [campuspulseai-agentic-loop-learning-guide.md](campuspulseai-agentic-loop-learning-guide.md) | Walkthrough of how to run and interpret the loop. |
| [student-4-financial-aid-billing.md](student-4-financial-aid-billing.md) | Financial Aid & Billing requirements, data design and risk register. |
| [agentic-loop-context/](agentic-loop-context/) | The approved project context supplied to the loop. |
| [agentic-loop-evidence/](agentic-loop-evidence/) | Captured run evidence in JSON. |

Solution-wide design is documented separately:

| Document | Purpose |
|---|---|
| [../architecture/system-architecture.md](../architecture/system-architecture.md) | Platform, layering, container diagram, integration map and known gaps. |
| [../architecture/decisions.md](../architecture/decisions.md) | Architecture decision records. |


## Service map

Ports are as declared in `docker-compose.yml`. Database services that publish no host
port are reachable only by service name on the Compose network.

| Owner | Service | Backend | Database | Frontend |
|---|---|---|---|---|
| student-1 | AcademicRecords | 5020 | 5021 | 8081 |
| student-2 | CourseManagement | not published | 5120 | 8082 |
| student-3 | CampusHousing | 5083 | not published | 8083 |
| student-4 | FinancialAidBilling | 8089 | not published | 8084 |
| student-5 | EventScheduling | 8090 | not published | 8085 |
| shared | shared-ui shell | — | — | 8080 |

The AI services (`mcpserver`, `multiagentserver`, `ragserver`) publish no host ports and
are consumed internally.

Every container listens on `8080` internally (`ASPNETCORE_HTTP_PORTS`), except the
frontends, which are served by nginx on port `80`.

## Data persistence

Named volumes declared in `docker-compose.yml`:

| Volume | Service |
|---|---|
| `coursemanagement-data` | student-2 |
| `campushousing-data` | student-3 |

Known gap: student-1, student-4 and student-5 have no named volume, so their SQLite
files are destroyed whenever the container is recreated. This is tracked for
Financial Aid & Billing as risk R001 in
[student-4-financial-aid-billing.md](student-4-financial-aid-billing.md) and affects
NFR-07 for each service concerned.

## Running the solution

```powershell
docker compose config --quiet
docker compose up --build
```

The shared shell is then available at <http://localhost:8080>, with each microservice
frontend reachable on its own port from the table above.

## Deterministic validation

The checks the agentic loop relies on can be run directly:

```powershell
dotnet restore CampusPulseAI.slnx
dotnet build CampusPulseAI.slnx
dotnet test CampusPulseAI.slnx
docker compose config --quiet
```

## Conventions

- Documentation uses Australian English spelling (organise, catalogue, licence,
  artefact, enrolment, prioritise).
- Code identifiers keep their framework-native spelling (`Color`, `InitializeAsync`,
  `AddAuthorization`) and must not be Australianised.
