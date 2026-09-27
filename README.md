# Maintenance Management System

A multi-tenant facilities maintenance management system: users raise
maintenance requests for sites, estimated costs are auto-approved or routed
to an approver based on a per-organization threshold, and every status change
is recorded in an append-only audit trail. Built as a technical evaluation
project — deliberately simple, reviewable, and testable.

- **Backend:** ASP.NET Core Web API on .NET 10 (single project — controllers
  + services, EF Core 10, SQL Server)
- **Frontend:** Angular 21 standalone application (signals, zoneless)
- **Tests:** 98 backend integration tests (xUnit, in-memory SQLite) +
  22 frontend unit tests (Vitest)

## Prerequisites

| Tool | Version used / required |
|---|---|
| .NET SDK | 10.x |
| SQL Server | any reachable instance (LocalDB or full; Windows auth works) |
| Node.js | 22.x (verified on 22.16.0) |
| npm | 10.9.2+ (pinned via `packageManager`) |
| Angular CLI | 21.x — local devDependency, no global install needed |
| Git | any recent version |

## Configuration (one-time)

Secrets are never committed. The API reads them from
[dotnet user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
(the project's `UserSecretsId` is already configured). From the repository
root, run:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=MaintenanceManagementSystem;Trusted_Connection=True;TrustServerCertificate=True" --project src/MaintenanceManagementSystem.Api

dotnet user-secrets set "Jwt:Issuer"     "maintenance-management-system" --project src/MaintenanceManagementSystem.Api
dotnet user-secrets set "Jwt:Audience"   "maintenance-management-system-api" --project src/MaintenanceManagementSystem.Api
dotnet user-secrets set "Jwt:SigningKey" "<random-string-of-32-or-more-characters>" --project src/MaintenanceManagementSystem.Api
dotnet user-secrets set "Jwt:LifetimeMinutes" "60" --project src/MaintenanceManagementSystem.Api
```

Notes:

- Replace the connection string with one valid for your SQL Server instance.
  Issuer/Audience/SigningKey values above are placeholders — any non-empty
  values work for local development.
- `Jwt:SigningKey` must be at least 32 characters. All `Jwt:*` values are
  validated at host startup (`ValidateOnStart`), so a missing or weak value
  fails fast with a descriptive error instead of misbehaving later.
- `Jwt:LifetimeMinutes` is optional (defaults to 60).

## Database

In the Development environment the API applies EF Core migrations and seeds
demo data automatically at startup (an idempotent seeder — a no-op once data
exists). Nothing needs to be created by hand; the database itself is created
by the migration.

The **test suite does not touch SQL Server**: integration tests host the real
API in-memory against an in-memory SQLite database created from the EF model,
with JWT settings supplied in-memory.

## Run

Backend (terminal 1, from the repository root):

```bash
dotnet run --project src/MaintenanceManagementSystem.Api
```

| URL | What |
|---|---|
| http://localhost:5295 | API (default `http` launch profile) |
| http://localhost:5295/health | anonymous health probe |
| http://localhost:5295/openapi/v1.json | OpenAPI document (Development only; machine-readable JSON — no interactive Swagger UI is installed) |

Frontend (terminal 2):

```bash
cd MaintenanceManagementSystem.Client
npm install
npm start            # ng serve
```

Open http://localhost:4200 and log in with a demo user below. The client is
preconfigured to call `http://localhost:5295/api`.

## Demo users

Seeded automatically in Development only. These are demo credentials for a
local evaluation project — never reuse this pattern in a real deployment.

| Organization (approval threshold) | Email | Role | Password |
|---|---|---|---|
| Northgate Facilities (1000.00) | `alice.requester@northgate.example` | Requester | `Pass123$` |
| Northgate Facilities (1000.00) | `bob.approver@northgate.example` | Approver | `Pass123$` |
| Summit Property Group (2500.00) | `carol.requester@summit.example` | Requester | `Pass123$` |
| Summit Property Group (2500.00) | `dave.approver@summit.example` | Approver | `Pass123$` |

Seeded sites: **HQ Tower** and **Riverside Depot** (Northgate Facilities),
**Summit Plaza** (Summit Property Group).

## What it does (a two-minute tour)

1. Log in as Alice (Requester, Northgate). Create a maintenance request for
   HQ Tower with an estimated cost of **250.00** — it comes back
   **Approved**: the cost is within the organization's 1000.00 threshold, so
   the system auto-approves it (the audit entry records a system action with
   no human approver).
2. Create another with cost **1500.00** — it lands in **PendingApproval**
   (above the threshold). Editing the cost of a pending request re-evaluates
   the threshold.
3. Log in as Bob (Approver, Northgate). Approve or reject the pending
   request; rejection requires a non-blank reason. Approvers cannot decide
   their own requests, and requesters get no decision controls — the UI
   merely hides what the server would refuse (the server enforces everything).
4. As Alice, complete an approved request with an actual cost.
5. Inspect the audit trail (`GET /api/audit`, read-only) and the spend
   report (`GET /api/reports/spend?from=&to=` — completed requests, actual
   costs, aggregated per site).
6. Log in as Carol (Summit). She sees none of Northgate's requests, sites,
   audit entries, or spend totals — tenant identity comes exclusively from
   her token, never from the client.

An example HTTP workflow against the API lives in
`src/MaintenanceManagementSystem.Api/MaintenanceManagementSystem.Api.http`.

## Tests

```bash
# Backend: 98 integration tests (SQL Server not required)
dotnet test MaintenanceManagementSystem.slnx

# Frontend: 22 unit tests (Vitest)
cd MaintenanceManagementSystem.Client
npm test
```

Both suites were green (98/98 and 22/22) in the verified submission state.

## Build

```bash
dotnet build MaintenanceManagementSystem.slnx
cd MaintenanceManagementSystem.Client && npm run build   # production output in dist/
```

## Architecture in brief

Single API project — no Clean Architecture/CQRS/MediatR/repository layers
(deliberate; see `DECISIONS.md` §2). Folder separation keeps the seams:

```
src/MaintenanceManagementSystem.Api/
  Domain/       entities, enums, RequestLifecycle (the only status mutator)
  Data/         AppDbContext, entity configurations, migrations, dev seeder
  Contracts/    request/response DTOs (domain types never cross the wire)
  Services/     feature services; business rules live here
  Auth/         JWT settings, token service, policies, TenantContext
  Controllers/  thin HTTP adapters
  Validation/   NotWhitespaceAttribute (rejects whitespace-only strings)
tests/MaintenanceManagementSystem.Tests/    integration tests over the real Program
MaintenanceManagementSystem.Client/         Angular 21 standalone SPA
```

Key properties:

- **Multi-tenancy:** every tenant-owned row carries `OrganizationId`;
  tenant identity comes only from JWT claims via a scoped `TenantContext`;
  EF Core global query filters scope list queries, and fetch-by-id
  re-validates ownership. Cross-tenant resources are indistinguishable from
  unknown ones (404 / empty list).
- **Secure by default:** a fallback authorization policy requires
  authentication on everything not explicitly anonymous (login, `/health`,
  the Development-only OpenAPI document). Roles, user ids, and organization
  ids supplied in request bodies are ignored — identity and role come only
  from the authenticated principal.
- **Lifecycle integrity:** all status changes flow through `RequestLifecycle`
  (illegal transitions get 409); every decision is audited in the same
  database transaction; system auto-approvals are recorded with a null actor.
- **Server owns state:** the client displays status, never computes it;
  approval decisions are a single `{ decision, reason? }` payload to
  `POST /api/maintenance-requests/{id}/approvals`.
- [`DECISIONS.md`](DECISIONS.md) documents 46 engineering decisions with
  reasons and rejected alternatives.

## Security notes

- Connection string and all JWT values live in user-secrets; nothing secret
  is in source control (`appsettings.json` contains logging configuration
  only).
- Passwords are hashed with ASP.NET Core's `PasswordHasher`; tokens are
  HS256 with `MapInboundClaims=false`, a 60-minute lifetime, and a 30-second
  clock skew.
- Known, documented v1 scope decisions: no refresh tokens, no login rate
  limiting/lockout, no token revocation, one role per user.

## Out of scope (v1)

User registration/management UI, refresh tokens, file attachments,
notifications, advanced paging/filtering, and any infrastructure beyond a
single API + SQL Server + SPA.

## Further reading

- [`DECISIONS.md`](DECISIONS.md) — every technical choice with its reason.
- [`AI-LOG.md`](AI-LOG.md) — how AI was used in this project, including a
  real prompt and a plausible-but-wrong AI output and how it was caught.
