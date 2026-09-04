# Student 3 — Campus Housing & Maintenance Plan and Traceability Report

## Purpose

This document updates the Student 3 project documentation so that it matches the code currently implemented under `student-3/` and satisfies the assessment requirement to document Agile planning, Student 3 backlog contributions, an individual feature plan, a risk plan, data design, and contribution to the overall team project plan.

## Service scope

Student 3 owns the **Campus Housing & Maintenance** microservice in [`student-3/`](../../student-3), including:

- backend gateway: [`student-3/backend/CampusHousing.Backend`](../../student-3/backend/CampusHousing.Backend)
- persistence service: [`student-3/database/CampusHousing.Database`](../../student-3/database/CampusHousing.Database)
- Blazor WebAssembly frontend: [`student-3/frontend/CampusHousing.Frontend`](../../student-3/frontend/CampusHousing.Frontend)
- automated tests: [`student-3/tests/CampusHousing.Tests`](../../student-3/tests/CampusHousing.Tests)
- AI prompt assets: [`student-3/prompts`](../../student-3/prompts)
- Docker/integration wiring: [`docker-compose.yml`](../../docker-compose.yml)

### Implemented service responsibilities

The current implementation provides:

1. **Dorm room inventory** browsing with building, room number, capacity, occupancy, and availability.
2. **Housing allocations** with student-scoped access, administrator management, conflict handling, and roommate preference capture.
3. **Maintenance ticketing** with optional room association, lifecycle updates, and administrator delete/update actions.
4. **AI-assisted maintenance triage** using Gemini when `GEMINI_API_KEY` is configured, with rule-based fallback when Gemini is unavailable, invalid, or returns non-normalized values.
5. **Role integration** using `X-Housing-Role` and `X-Housing-User` headers and user data sourced from Academic Records.
6. **Seed/demo data and automated tests** across database constraints, controller authorization/filtering, and maintenance classification.

### Implemented now vs planned backlog

| Area | Current state | Notes |
| --- | --- | --- |
| Room browse, room details, and room CRUD | Implemented | Admin-only room create/update/delete is enforced in the backend gateway. |
| Student allocation submission and admin reallocation/cancellation | Implemented | Duplicate student allocations are blocked by application logic and a unique index. |
| Roommate preference capture | Implemented | Preferences are stored as free text and visible/editable to administrators. |
| Roommate matching or pairing logic | Planned | No automatic matching, compatibility scoring, or roommate assignment workflow exists in code. |
| Maintenance ticket submission and lifecycle updates | Implemented | Students submit tickets; administrators update status, severity, queue, or delete. |
| AI severity and queue classification | Implemented | Gemini plus normalization and rule fallback are present. |
| Dedicated emergency override field | Planned | Not present in the current forms, DTOs, or persistence model. |
| Capacity protection under concurrent booking load | Partial | Current code validates capacity before save, but no proven database row locking or explicit transactional overbooking protection is evidenced. |
| Performance evidence under 2 seconds | Not evidenced | No latency logs or benchmark artefacts are stored in the repository. |
| UI accessibility evidence | Not evidenced | Labels and semantic controls exist, but no documented audit, screenshots, or automated accessibility checks are present. |

### Primary repository evidence

- Gateway/controller: [`HousingController.cs`](../../student-3/backend/CampusHousing.Backend/Controllers/HousingController.cs)
- Maintenance classifier: [`MaintenanceClassifier.cs`](../../student-3/backend/CampusHousing.Backend/Services/MaintenanceClassifier.cs)
- Database API and persistence rules: [`Program.cs`](../../student-3/database/CampusHousing.Database/Program.cs)
- EF Core model and constraints: [`CampusHousingDbContext.cs`](../../student-3/database/CampusHousing.Database/Data/CampusHousingDbContext.cs)
- Seed data: [`CampusHousingSeedData.cs`](../../student-3/database/CampusHousing.Database/Data/CampusHousingSeedData.cs)
- Frontend pages: [`Home.razor`](../../student-3/frontend/CampusHousing.Frontend/Pages/Home.razor), [`Rooms.razor`](../../student-3/frontend/CampusHousing.Frontend/Pages/Rooms.razor), [`Allocations.razor`](../../student-3/frontend/CampusHousing.Frontend/Pages/Allocations.razor), [`Maintenance.razor`](../../student-3/frontend/CampusHousing.Frontend/Pages/Maintenance.razor)
- Role context: [`HousingUserContext.cs`](../../student-3/frontend/CampusHousing.Frontend/Services/HousingUserContext.cs)
- Tests: [`HousingControllerTests.cs`](../../student-3/tests/CampusHousing.Tests/HousingControllerTests.cs), [`CampusHousingDatabaseTests.cs`](../../student-3/tests/CampusHousing.Tests/CampusHousingDatabaseTests.cs), [`MaintenanceClassifierTests.cs`](../../student-3/tests/CampusHousing.Tests/MaintenanceClassifierTests.cs)

## Requirement baseline used in this report

This report uses the supplied **FR-01 to FR-33** and **NFR-01 to NFR-15** catalogue as the authoritative requirement list.

Earlier planning text used inconsistent feature ranges such as `RES-2`, `RES-3`, and older FR mappings. Those ranges are superseded here by the supplied requirement IDs and are mapped conservatively to repository evidence. A requirement is marked:

- **Implemented** when code and supporting evidence exist in the repository.
- **Partial** when part of the behavior exists, but evidence or completeness is missing.
- **Planned** when it is a sensible backlog item but is not implemented.
- **Not evidenced** when the requirement may be desirable but the repository does not currently prove it.

## Agile product and sprint backlog

The table below shows a proposed Student 3 backlog based on the implemented service and the remaining gaps. `Owner / contributor` identifies whether the item is primarily Student 3 or a team/shared integration item.

| Story ID | Priority | Requirement IDs | Backlog item | Owner / contributor | Acceptance criteria | Status | Verification evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S3-01 | Must | FR-01, FR-02, FR-06, NFR-02, NFR-12 | Browse dorm rooms with occupancy and availability indicators | Student 3 | Users can load rooms, see building/room/capacity/occupancy, and understand when a room cannot be booked | Partial | Room data is returned by the database API and shown in `Home.razor` and `Rooms.razor`; the UI marks rooms as `Available` or `Full`, but does not distinguish disabled rooms from full rooms with separate wording |
| S3-02 | Must | FR-03, FR-07, FR-08, FR-33, NFR-01, NFR-02, NFR-03, NFR-09 | Submit housing allocation requests with duplicate-allocation protection | Student 3 | Students can request a room; duplicate allocations are rejected; successful submission resets the form; users receive a conflict or reallocation message | Implemented | Allocation endpoints in database `Program.cs`; conflict handling in backend/controller; reallocation confirmation and form reset in `Home.razor`; controller and database tests cover conflicts and unique allocation enforcement |
| S3-03 | Must | FR-04, FR-05, FR-30, NFR-03, NFR-04, NFR-09 | Administrator room inventory management | Student 3 | Admin can create, edit, disable, and delete room records; students cannot perform those actions | Implemented | Admin-only checks in `HousingController.cs`; CRUD endpoints in database `Program.cs`; admin form in `Rooms.razor`; controller tests cover create/update/delete authorization |
| S3-04 | Should | FR-09, FR-10, FR-11, FR-12, FR-13, FR-14, NFR-04 | Allocation administration and roommate preference management | Student 3 | Admin can view all allocations, change room assignments, cancel allocations, and view/update roommate preferences beside each allocation | Implemented | Allocation list/update/delete in backend and database services; roommate preference field stored in DTO/entity models; admin UI in `Allocations.razor` and `Home.razor` |
| S3-05 | Could | FR-12, FR-13, FR-14 | Roommate matching and pairing workflow | Student 3 | Preferences are not only stored but used to suggest or assign compatible roommates | Planned | No pairing engine, compatibility rules, or roommate-assignment output exists in the current repository |
| S3-06 | Must | FR-15, FR-16, FR-17, FR-26, FR-33, NFR-03, NFR-12 | Student maintenance submission and history | Student 3 | Students can submit tickets with optional room association, see ticket history, and receive success/error messages; successful submission resets the form | Implemented | Ticket creation/history UI in `Maintenance.razor` and `Home.razor`; ticket DTO allows nullable `DormRoomId`; backend validates description and classifies tickets before persistence |
| S3-07 | Must | FR-18, FR-19, FR-20, FR-21, FR-30, NFR-04, NFR-09 | Administrator maintenance lifecycle management | Student 3 | Admin can see all tickets, update status/severity/queue, and delete obsolete tickets | Implemented | Ticket admin actions in `HousingController.cs`, database `Program.cs`, `Maintenance.razor`, and `Home.razor`; controller tests cover update/delete authorization |
| S3-08 | Must | FR-22, FR-23, FR-24, FR-25, NFR-07 | AI triage and resilient classification | Student 3 | Ticket descriptions are classified into severity and repair queue; fallback remains available when Gemini is absent or invalid | Implemented | `MaintenanceClassifier.cs` plus `MaintenanceClassifierTests.cs`; current implementation uses Gemini, not Ollama |
| S3-09 | Should | FR-27, FR-28, FR-29, FR-30, FR-32, NFR-10, NFR-13 | Shared CampusPulse integration and role-sensitive UX | Student 3 with team/shared dependencies | Housing runs inside the shared shell, supports student/admin views, and shows loading/error/empty states consistently | Partial | `MainLayout.razor`, `NavMenu.razor`, `HousingUserContext.cs`, and compose services show integration intent, but complete end-to-end shared-platform evidence is limited |
| S3-10 | Must | NFR-01, NFR-06, NFR-14, NFR-15 | Persistence, separation of concerns, and source-control hygiene | Student 3 | Constraints are enforced, the SQLite file persists in Docker storage, secrets and local DB files are excluded from source control, and projects stay modular | Implemented | EF indexes in `CampusHousingDbContext.cs`, compose volume/path in `docker-compose.yml`, `.gitignore` excludes `*.db`, and the repo layout remains split by frontend/backend/database/tests |
| S3-11 | Should | NFR-05, NFR-08, NFR-10, NFR-11, NFR-12, NFR-13 | Demonstrate operational quality with evidence | Student 3 with team/shared validation | Restart persistence, response-time targets, accessibility checks, and UI evidence are captured and attached to project artefacts | Partial | Some UX and persistence mechanisms exist, but the repository does not include restart demonstrations, latency logs, or accessibility evidence |
| S3-12 | Should | NFR-02 | Concurrency hardening for final-bed allocation | Student 3 | Allocation writes prevent overbooking under concurrent requests with proven transaction/locking behavior | Planned | Current code performs capacity checks before saving, but no explicit row locking or concurrency test is present |
| S3-13 | Should | FR-22, FR-25 | Manual emergency override for hazardous maintenance reports | Student 3 | Staff or students can explicitly escalate hazardous tickets without relying only on automated classification | Planned | No dedicated emergency override field exists in forms, DTOs, or persistence models |
| S3-14 | Must | Assessment planning requirement | Document Student 3 backlog, feature plan, risks, data design, and project-plan contribution | Student 3 | A repository document accurately reflects implemented behavior and explicitly marks gaps | Implemented | This report and the README documentation link |

## Student 3 individual feature plan

### Completed or substantially completed work

| Feature area | Scope covered now | Evidence |
| --- | --- | --- |
| Dorm room catalogue | Room list, occupancy count, availability flag, admin room CRUD | Database room endpoints, `Rooms.razor`, `Home.razor` |
| Housing allocations | Create, list, update, delete, student filtering, admin view, conflict handling, reallocation prompt | `HousingController.cs`, database allocation endpoints, `Allocations.razor`, `Home.razor` |
| Roommate preference capture | Free-text preference stored with allocation and editable by administrators | DTOs/models plus allocation pages |
| Maintenance lifecycle | Create ticket, optional room link, list history, admin update/delete, resolved timestamp | Database ticket endpoints, backend controller, `Maintenance.razor`, `Home.razor` |
| AI classification and routing | Gemini classification, JSON normalization, rule fallback, visible severity/queue | `MaintenanceClassifier.cs`, ticket pages, classifier tests |
| Role integration | Academic Records user list, student/admin selector, header-based context propagation, filtered results | `HousingUserContext.cs`, `HousingRoleHandler`, `HousingController.cs` |
| Feedback and loading | Messages for success/error/empty states and busy/loading indicators in the shell and pages | `FeedbackPanel.razor`, `LoadingIndicator.razor`, page-level message/loading states |
| Testing | Database constraint tests, controller tests, classifier tests | `CampusHousing.Tests` project |
| Documentation | This report and README link | `docs/reports/...`, `README.md` |

### Partial work that should be carried forward

| Feature area | Current limitation | Next action |
| --- | --- | --- |
| Availability wording | Disabled rooms and full rooms are both surfaced as unavailable/full rather than differentiated explicitly | Add distinct UI labels such as `Unavailable` versus `Full` and verify with UI evidence |
| Roommate workflow | Preference text is captured but not matched or paired | Define roommate matching rules, decision criteria, and override workflow |
| Shared UX consistency | Shared loading/feedback components are present but not used uniformly across all pages | Standardize all page notices and busy states on shared components |
| Operational evidence | Performance and accessibility expectations are not backed by repository artefacts | Add a lightweight evidence pack or repeatable test checklist |
| Concurrency protection | Capacity checks exist but concurrency behavior is not proven | Add transactional/concurrency design and verification for final-bed allocation |

### Proposed next sprint plan

| Sprint goal | Planned stories | Definition of done |
| --- | --- | --- |
| Sprint A: close requirement-evidence gaps | S3-09, S3-11, S3-14 | Integration documentation updated, role flows demonstrated, missing proof items identified with owners |
| Sprint B: housing robustness | S3-12, S3-13 | Concurrency mitigation designed/tested; emergency escalation path defined and implemented if approved |
| Sprint C: roommate workflow enhancement | S3-05 | Preference model extended into an actual matching or staff-assisted pairing process |

## Risk register

| Risk ID | Risk | Likelihood | Impact | Mitigation | Contingency / monitoring | Owner | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R1 | AI misclassification could downgrade a hazardous issue | Medium | High | Keep Gemini output normalized to approved values and retain rule-based fallback when Gemini is missing, fails, or returns invalid data | Review hazardous-keyword tickets manually; add an emergency override backlog item if approved | Student 3 | Active |
| R2 | Concurrent booking of the last available bed could over-allocate a room | Medium | High | Current code checks availability and capacity before save | Add explicit concurrency/transaction proof and monitor for over-capacity records during testing; backlog item S3-12 remains open | Student 3 | Active |
| R3 | Academic Records startup dependency may delay housing initialization | Medium | Medium | Database service retries Academic Records user retrieval up to 15 times during startup | Monitor container health and startup logs; fall back to seeded local demo users only if a design change is approved | Student 3 with team/shared support | Active |
| R4 | Shared integration assumptions may drift across student services | Medium | Medium | Keep role headers, compose wiring, and shell navigation aligned with team conventions | Review integration in sprint reviews and maintain a shared interface contract | Team/shared | Active |
| R5 | Raw API error payloads may reduce usability for end users | Medium | Medium | Continue surfacing validation/conflict messages and plan UX polishing for server-generated error text | Capture user feedback during demo rehearsal and improve message formatting in a later sprint | Student 3 | Active |
| R6 | Accessibility compliance may be claimed without evidence | Medium | Medium | Keep semantic labels and keyboard-friendly controls in place | Perform manual accessibility review and record findings before final submission | Student 3 | Open |
| R7 | Local persistence could be lost if Docker volumes are misconfigured outside the repository defaults | Low | Medium | Persist SQLite to `/app/data/campus-housing.db` on the `campushousing-data` volume | Validate compose deployment instructions and inspect mounted volume during acceptance testing | Student 3 | Mitigated by design |

## Data design

### Conceptual model

The Campus Housing domain has three persisted business entities and two external integration concepts:

- **Dorm room**: a housing inventory record identified by building and room number, with capacity and availability state.
- **Housing allocation**: a student's active room assignment request/record, linked to one dorm room and carrying a student identifier, student name snapshot, roommate preference text, and allocation timestamp.
- **Maintenance ticket**: a student-submitted issue, optionally linked to a dorm room, carrying description, severity, repair queue, status, and lifecycle timestamps.
- **Academic Records user (external source)**: the authoritative source for student identity and role-aware user selection. The housing database does **not** hold a foreign key to Academic Records.
- **Role context (integration metadata)**: `X-Housing-Role` and `X-Housing-User` headers determine whether the backend should expose all records or only the current student's records.

### Mermaid ERD

```mermaid
erDiagram
    ACADEMIC_RECORDS_USER {
        int UserId
        string StudentId
        string FirstName
        string LastName
        string Role
    }

    DORM_ROOM {
        int Id PK
        string Building
        string RoomNumber
        int Capacity
        bool IsAvailable
    }

    HOUSING_ALLOCATION {
        int Id PK
        int DormRoomId FK
        string StudentId
        string StudentName
        string RoommatePreference
        datetime AllocatedAt
    }

    MAINTENANCE_TICKET {
        int Id PK
        string StudentId
        int DormRoomId FK_nullable
        string Description
        string Severity
        string RepairQueue
        string Status
        datetime CreatedAt
        datetime ResolvedAt_nullable
    }

    DORM_ROOM ||--o{ HOUSING_ALLOCATION : contains
    DORM_ROOM o|--o{ MAINTENANCE_TICKET : reported_for
    ACADEMIC_RECORDS_USER ||--o{ HOUSING_ALLOCATION : identified_by
    ACADEMIC_RECORDS_USER ||--o{ MAINTENANCE_TICKET : reported_by
```

### Logical data dictionary

#### DormRoom

| Attribute | Type | Required | Notes |
| --- | --- | --- | --- |
| `Id` | integer | Yes | Primary key |
| `Building` | text | Yes | Part of the unique room identity |
| `RoomNumber` | text | Yes | Part of the unique room identity |
| `Capacity` | integer | Yes | Must be positive in API validation |
| `IsAvailable` | boolean | Yes | Indicates whether the room accepts new allocations |
| `OccupiedBeds` | derived integer | No | Computed in the database API response from allocation count; not stored in the entity |

#### HousingAllocation

| Attribute | Type | Required | Notes |
| --- | --- | --- | --- |
| `Id` | integer | Yes | Primary key |
| `DormRoomId` | integer | Yes | References the selected room inside the housing database |
| `StudentId` | text | Yes | Unique across allocations; sourced from Academic Records/user context |
| `StudentName` | text | Yes | Stored snapshot rather than external foreign key lookup |
| `RoommatePreference` | text | No | Free-text preference capture only |
| `AllocatedAt` | UTC datetime | Yes | Reset on create and on room change |

#### MaintenanceTicket

| Attribute | Type | Required | Notes |
| --- | --- | --- | --- |
| `Id` | integer | Yes | Primary key |
| `StudentId` | text | Yes | Student-scoped ownership field |
| `DormRoomId` | integer | No | Optional room context |
| `Description` | text | Yes | Required for classification and persistence |
| `Severity` | text | Yes | Normalized to `Emergency`, `High`, `Medium`, or `Low` |
| `RepairQueue` | text | Yes | Normalized to `Heating`, `Plumbing`, `Electrical`, `Security`, or `General Maintenance` |
| `Status` | text | Yes | Open workflow values are driven by UI and persistence logic |
| `CreatedAt` | UTC datetime | Yes | Set on create |
| `ResolvedAt` | UTC datetime | No | Set when status becomes `Resolved` |

### Physical design and constraints

| Area | Current implementation |
| --- | --- |
| Database engine | SQLite through EF Core |
| Database project | `student-3/database/CampusHousing.Database` |
| Container path | `/app/data/campus-housing.db` |
| Docker volume | `campushousing-data` mounted to `/app/data` |
| Room identity constraint | Unique index on `(Building, RoomNumber)` |
| Student allocation constraint | Unique index on `HousingAllocation.StudentId` |
| Capacity enforcement | Application-level checks reject missing rooms, unavailable rooms, and rooms at capacity before save |
| Ticket normalization | Backend classifier normalizes AI output to approved severity and repair queue values |
| Seed data | 10 seeded rooms, 10 seeded allocations, and 10 seeded maintenance tickets |
| Referential design | Allocations and tickets store `StudentId`; allocations also store `StudentName` as a local snapshot because Academic Records is external |

### Integration boundaries

| Boundary | Source | Destination | Contract |
| --- | --- | --- | --- |
| UI to backend gateway | Campus Housing frontend | Housing backend | HTTP calls to `/api/housing/*` with role headers injected by `HousingRoleHandler` |
| Backend to housing persistence | Housing backend | Housing database service | Backend forwards room/allocation/ticket operations to database endpoints |
| Backend/database startup to user source | Housing backend and database seed startup | Academic Records service | User list loaded from Academic Records; seed data uses those users when available |
| Backend to AI provider | Housing backend | Gemini API | Classification request to Gemini; fallback rules used when unavailable or invalid |

### Data-integrity implications

- **Academic Records remains the source of truth for users.** Campus Housing consumes user data but does not own the master user table.
- **Allocations store student identifiers and names locally.** This simplifies service autonomy and seeded demo data, but introduces possible name staleness if Academic Records changes later.
- **Unique indexes protect core invariants.** The repository proves one unique room identity and one active allocation per student.
- **Capacity control is application-enforced, not fully concurrency-proven.** The current checks are valuable, but additional transactional proof would be needed to claim hardened final-bed protection.

## Requirement traceability matrix

### Functional requirements

| ID | Requirement summary | Status | Relevant component / file(s) | Verification approach |
| --- | --- | --- | --- | --- |
| FR-01 | Student can browse available dorm rooms | Implemented | `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Home.razor`, `CampusHousing.Frontend/Pages/Rooms.razor` | Call `GET /api/housing/rooms` and confirm the room list renders in the UI |
| FR-02 | Student can view building, room number, capacity, and occupancy | Implemented | `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Models/HousingModels.cs`, `CampusHousing.Frontend/Pages/Home.razor`, `CampusHousing.Frontend/Pages/Rooms.razor` | Verify API payload includes occupancy and that cards/tables show capacity and occupied beds |
| FR-03 | Student can submit a room allocation request | Implemented | `CampusHousing.Backend/Controllers/HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Home.razor` | Submit a valid allocation request and confirm `201 Created` plus UI success message |
| FR-04 | Staff can create, edit, and delete dorm rooms | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Rooms.razor` | Use admin role, create/update/delete a room, and confirm controller tests still pass |
| FR-05 | Staff can update room capacity and availability | Implemented | `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Rooms.razor` | Update `Capacity` and `IsAvailable` from the admin form and confirm persistence |
| FR-06 | Full or disabled rooms are clearly marked unavailable | Partial | `CampusHousing.Frontend/Pages/Home.razor`, `CampusHousing.Frontend/Pages/Rooms.razor` | UI currently marks non-bookable rooms as `Full`; proof of separate disabled/unavailable wording would be needed for full completion |
| FR-07 | Student cannot create multiple active allocations | Implemented | `CampusHousing.Database/Data/CampusHousingDbContext.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Tests/CampusHousingDatabaseTests.cs` | Verify duplicate allocation attempts return conflict and duplicate database inserts fail |
| FR-08 | Student sees a clear conflict message when already allocated | Implemented | `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Home.razor` | Attempt a second allocation for the same student and confirm conflict/reallocation messaging |
| FR-09 | Administrator can view all allocations | Implemented | `HousingController.cs`, `CampusHousing.Frontend/Pages/Allocations.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Use admin role and verify all allocations are returned unfiltered |
| FR-10 | Administrator can change a student's allocated room | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Allocations.razor` | Change a room assignment as admin and verify the updated room is saved |
| FR-11 | Administrator can cancel a room allocation | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Allocations.razor` | Delete an allocation as admin and confirm room availability updates |
| FR-12 | Student can submit a roommate preference with a housing request | Implemented | `CampusHousing.Backend/Models/HousingDtos.cs`, `CampusHousing.Database/Models/HousingModels.cs`, `CampusHousing.Frontend/Pages/Home.razor` | Submit an allocation with roommate preference text and confirm it is stored/displayed |
| FR-13 | Administrator can view and update roommate preference | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Allocations.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Edit the preference as admin and confirm the saved value reloads |
| FR-14 | Administrator can review roommate preferences beside allocations | Implemented | `CampusHousing.Frontend/Pages/Allocations.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Open the admin allocations view and confirm the roommate preference column is visible |
| FR-15 | Student can submit a maintenance ticket | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Submit a ticket and confirm it is persisted and listed |
| FR-16 | Student can optionally associate a ticket with a dorm room | Implemented | `CampusHousing.Backend/Models/HousingDtos.cs`, `CampusHousing.Database/Models/HousingModels.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor` | Submit with and without `DormRoomId` and confirm both paths work |
| FR-17 | Student can view ticket status, severity, queue, description, and creation date | Implemented | `HousingController.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Load the student ticket view and verify all listed fields are shown |
| FR-18 | Administrator can view all maintenance tickets | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Use admin role and verify all tickets are visible, not student-filtered |
| FR-19 | Administrator can update ticket status through lifecycle values | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor` | Change a ticket to `Open`, `In Progress`, or `Resolved` and confirm persistence/resolved timestamp behavior |
| FR-20 | Administrator can update severity and repair queue | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor` | Edit severity and queue as admin and verify saved values |
| FR-21 | Administrator can delete a maintenance ticket | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor` | Delete a ticket as admin and confirm it is removed |
| FR-22 | Description is analysed automatically for severity | Implemented | `HousingController.cs`, `MaintenanceClassifier.cs`, `MaintenanceClassifierTests.cs` | Create a ticket and confirm classifier output is included before persistence |
| FR-23 | Ticket is assigned to a repair queue | Implemented | `MaintenanceClassifier.cs`, `CampusHousing.Frontend/Pages/Maintenance.razor` | Verify classifier or fallback assigns one approved repair queue |
| FR-24 | Severity and repair queue are visible on tickets | Implemented | `CampusHousing.Frontend/Pages/Maintenance.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Confirm ticket listings show severity and queue values |
| FR-25 | Rule-based fallback is used when AI is unavailable or invalid | Implemented | `MaintenanceClassifier.cs`, `MaintenanceClassifierTests.cs` | Run classifier tests covering transport failure, invalid JSON, empty response, and invalid values |
| FR-26 | User receives success or error feedback after ticket submission | Implemented | `CampusHousing.Frontend/Pages/Maintenance.razor`, `CampusHousing.Frontend/Pages/Home.razor` | Submit valid and invalid tickets and confirm messages are shown |
| FR-27 | Campus Housing is accessible from the shared CampusPulse application | Partial | `CampusHousing.Frontend/Layout/MainLayout.razor`, `CampusHousing.Frontend/Layout/NavMenu.razor`, `docker-compose.yml` | Compose wiring and shared shell are present, but full end-to-end proof from the shared platform should be demonstrated in an integrated run |
| FR-28 | Application identifies student versus administrator context | Implemented | `HousingUserContext.cs`, `HousingRoleHandler`, `HousingController.cs` | Change selected user and inspect outgoing headers and resulting UI permissions |
| FR-29 | Student sees only own allocations and maintenance tickets | Implemented | `HousingController.cs`, `HousingControllerTests.cs` | Run controller tests for filtered allocations/tickets and manually verify student views |
| FR-30 | Administrator can view and manage all rooms, allocations, and tickets | Implemented | `HousingController.cs`, `Rooms.razor`, `Allocations.razor`, `Maintenance.razor` | Use admin mode and confirm CRUD/manage actions across all three areas |
| FR-31 | Validation and conflict messages are clear during allocation and ticket submission | Partial | `CampusHousing.Database/Program.cs`, `HousingController.cs`, `Home.razor`, `Maintenance.razor` | Validation/conflict paths exist, but manual UX review is still needed because some server errors surface as raw JSON strings |
| FR-32 | UI provides loading, empty, success, and error feedback | Partial | `FeedbackPanel.razor`, `LoadingIndicator.razor`, `MainLayout.razor`, `Rooms.razor`, `Allocations.razor`, `Maintenance.razor`, `Home.razor` | Loading and messages exist, but a complete UI evidence set and consistent shared-component use would be needed for full proof |
| FR-33 | Allocation and maintenance forms reset after success | Implemented | `CampusHousing.Frontend/Pages/Home.razor`, `CampusHousing.Frontend/Pages/Maintenance.razor` | Submit valid requests and confirm form values are cleared |

### Non-functional requirements

| ID | Requirement summary | Status | Relevant component / file(s) | Verification approach |
| --- | --- | --- | --- | --- |
| NFR-01 | Unique room identity and one allocation per student | Implemented | `CampusHousingDbContext.cs`, `CampusHousingDatabaseTests.cs` | Run database tests covering model indexes and duplicate insert rejection |
| NFR-02 | Reject allocations for missing, unavailable, or full rooms | Implemented | `CampusHousing.Database/Program.cs` | Exercise allocation API for nonexistent room, unavailable room, and at-capacity room cases |
| NFR-03 | Reject missing student IDs, names, descriptions, invalid rooms, and missing status | Implemented | `CampusHousing.Database/Program.cs`, `HousingController.cs`, `HousingControllerTests.cs` | Run controller/database validation tests and manual invalid payload checks |
| NFR-04 | Students are restricted to their own data; admins manage all | Implemented | `HousingController.cs`, `HousingUserContext.cs`, `HousingControllerTests.cs` | Run authorization/filtering tests and inspect student/admin behavior |
| NFR-05 | Data remains available after backend restart | Partial | `docker-compose.yml`, `CampusHousing.Database/Program.cs` | Persistence design exists, but a restart-and-reload demonstration or automated test is still required |
| NFR-06 | SQLite database persists on a Docker-mounted volume | Implemented | `docker-compose.yml` | Inspect compose configuration and confirm `/app/data/campus-housing.db` is stored on `campushousing-data` |
| NFR-07 | AI classification continues working when Gemini is unavailable or invalid | Implemented | `MaintenanceClassifier.cs`, `MaintenanceClassifierTests.cs` | Run fallback and invalid-response classifier tests; the current code path is Gemini-based with rule fallback |
| NFR-08 | Standard operations respond within two seconds locally | Not evidenced | No repository artefact currently proves this | Capture Postman/browser latency logs before claiming completion |
| NFR-09 | API returns correct HTTP status codes for invalid, forbidden, missing, and conflict states | Implemented | `HousingController.cs`, `CampusHousing.Database/Program.cs`, `HousingControllerTests.cs` | Run controller tests and manual API checks for `400`, `403`, `404`, and `409` |
| NFR-10 | Frontend shows loading/busy indication during data load or processing | Partial | `LoadingIndicator.razor`, `MainLayout.razor`, `Rooms.razor`, `Allocations.razor`, `Maintenance.razor`, `Home.razor` | Some loading/busy states exist, but full UI demonstration evidence is still needed |
| NFR-11 | Forms and interactions are accessible | Partial | `MainLayout.razor`, `NavMenu.razor`, `Rooms.razor`, `Maintenance.razor`, `Home.razor` | Semantic labels/controls are present, but manual accessibility review evidence is not yet stored |
| NFR-12 | Interface provides clear empty, success, and failure messaging | Partial | `Rooms.razor`, `Allocations.razor`, `Maintenance.razor`, `Home.razor` | Empty and success states are present, but message clarity should be confirmed because some failures appear as raw JSON |
| NFR-13 | Housing pages use shared shell, navigation, layout, and feedback patterns | Partial | `MainLayout.razor`, `NavMenu.razor`, shared UI references in frontend project | Shared shell/layout is used, but feedback/loading patterns are not completely standardized across every page |
| NFR-14 | Secrets, connection strings, and local DB files stay out of source control | Implemented | `.gitignore`, `docker-compose.override.yml` | Audit ignored `*.db` files and user-secrets mounts; confirm no keys or DB artefacts are committed |
| NFR-15 | Housing code remains separated by frontend/backend/database/tests | Implemented | Repository structure under `student-3/` | Review project layout and confirm layers can be built/tested independently |

## Contribution to the overall project plan

### Student 3 dependencies and integration points

| Dependency | Why it matters | Student 3 contribution |
| --- | --- | --- |
| Academic Records service | Supplies student directory data used for user selection and seed alignment | Maintain the `/api/housing/users` gateway path and keep startup integration stable |
| Shared CampusPulse shell | Provides a consistent cross-service UI frame and navigation model | Keep housing pages aligned with shared shell and navigation conventions |
| Docker Compose | Required for integrated demonstration and persistence | Maintain `campushousing.backend`, `campushousing.database`, and `campushousing.frontend` service wiring |
| Gemini configuration | Enables AI classification when configured | Provide safe fallback behavior when the API key is absent or Gemini fails |
| Team quality gates | Needed for integrated demonstration and assessment evidence | Supply targeted tests, traceability, and documentation for Student 3 deliverables |

### Proposed milestones and sprint structure

No repository evidence proves actual sprint dates, so the following is a **planning template** rather than a historical claim.

| Sprint / milestone | Objective | Student 3 deliverables |
| --- | --- | --- |
| Sprint 0 | Confirm scope, requirements, interfaces, and data boundaries | FR/NFR contribution, initial conceptual model, integration contract with Academic Records |
| Sprint 1 | Establish persistence and baseline UI | Room model, allocation model, room list, seed data, base navigation |
| Sprint 2 | Deliver role-aware housing workflows | Allocation submission, admin room/allocation management, student filtering |
| Sprint 3 | Deliver maintenance lifecycle and AI classification | Ticket submission/history, admin updates, Gemini fallback classification |
| Sprint 4 | Harden quality, evidence, and integration | Traceability, risk review, persistence checks, accessibility/performance evidence, final documentation |

### Definition of Ready

A Student 3 story is ready when:

- the requirement ID(s) and owner are agreed,
- dependencies on Academic Records, shared UI, or compose wiring are understood,
- acceptance criteria are testable,
- data impacts are identified,
- security/privacy implications are known,
- and verification evidence is defined before development begins.

### Definition of Done

A Student 3 story is done when:

- code is implemented in the correct student-3 layer,
- role behavior is verified for student and administrator contexts where relevant,
- automated tests or documented manual checks are updated,
- Docker/integration impact is reviewed,
- documentation and traceability are updated,
- and any remaining gap is explicitly marked `Partial`, `Planned`, or `Not evidenced` rather than implied complete.

### Ceremony cadence template

| Ceremony | Proposed cadence | Student 3 contribution |
| --- | --- | --- |
| Backlog refinement | Weekly | Reconfirm FR/NFR mapping, dependencies, and acceptance criteria |
| Sprint planning | Start of sprint | Commit to housing stories and identify shared blockers |
| Daily stand-up | Each working day | Report housing progress, blockers, and integration needs |
| Sprint review | End of sprint | Demonstrate implemented housing workflows and evidence |
| Retrospective | End of sprint | Capture process improvements, quality gaps, and risk updates |

### Useful project metrics template

The repository does not contain authoritative historical metrics, so the following should be tracked as a template for team reporting:

| Metric | Why it is useful | Suggested measurement |
| --- | --- | --- |
| Velocity | Shows how much planned work the team completes each sprint | Story points or completed backlog items per sprint |
| Test pass rate | Shows whether code changes remain stable | Passed tests divided by executed tests for Student 3 CI and team CI |
| Defects escaped | Shows quality gaps discovered after a sprint or integration | Count of post-sprint bugs or demo defects |
| Cycle time | Shows how quickly work moves from ready to done | Average elapsed time per story |
| Requirement coverage | Shows whether assessment artefacts remain aligned with implementation | Percentage of FR/NFR rows with current evidence |

## Verification notes for this documentation update

This report was based on a direct review of the current Student 3 source code, test project, compose wiring, and repository documentation. Where the repository did not provide proof for a claim, the requirement has been marked `Partial`, `Planned`, or `Not evidenced` instead of being overstated.
