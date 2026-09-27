# CLAUDE.md — Agent Rules for This Repository

Rules for any AI agent (Claude or otherwise) working in this repository.
These reflect the workflow actually used to build this project. When a
human instruction conflicts with this file, the human instruction wins.

## Project

- **Name:** `MaintenanceManagementSystem` everywhere (solution, projects,
  namespaces, folders, database, docs). Never "MaintServ".
- **What:** multi-tenant facilities maintenance system — maintenance
  requests with cost-based approval thresholds, per-organization tenant
  isolation, append-only audit trail, spend reporting, and an Angular SPA.
- **Context:** a technical evaluation project. Prefer simple, reviewable,
  defensible code over maximum abstraction.
- **Stack:** .NET 10 / ASP.NET Core controllers / EF Core 10 / SQL Server;
  Angular 21 standalone (signals, zoneless) with Vitest; xUnit +
  `WebApplicationFactory` for integration tests.

## Architecture rules

- **One backend project** (`src/MaintenanceManagementSystem.Api`) with
  folder separation (`Domain/`, `Data/`, `Contracts/`, `Services/`,
  `Auth/`, `Controllers/`, `Validation/`). Do not add class libraries,
  Clean Architecture, CQRS, MediatR, repository-per-entity, caching, or
  background jobs.
- Domain rules must not depend on EF or HTTP. Controllers stay thin;
  business rules live in `Services/`.
- All status changes flow through `Domain/RequestLifecycle` — never set
  status directly. Illegal transitions are 409s.
- Every state-changing business operation writes its audit entries in the
  same transaction, via `AuditService`.

## Security rules

- **No secrets in source control. Ever.** Connection string
  (`ConnectionStrings:Default`) and JWT settings (`Jwt:Issuer`,
  `Jwt:Audience`, `Jwt:SigningKey`, `Jwt:LifetimeMinutes`) live in
  `dotnet user-secrets` on the API project. Never print secret values.
- Tenant/role identity comes only from the authenticated principal via
  `TenantContext`. DTOs must never carry `OrganizationId`, `Role`, or user
  identity fields; ignore any the client sends.
- Keep the secure-by-default fallback authorization policy; only login,
  `/health`, and the Development-only OpenAPI document are anonymous.
- No `IgnoreQueryFilters()` in production code (the only allowed use is
  `DbSeeder`, which runs outside any request). Fetch-by-id re-validates
  tenant ownership; cross-tenant resources stay indistinguishable from
  unknown (404 / empty).
- Never "fix" security in the client. Client-side checks are
  presentation-only; the server is authoritative.

## Workflow rules

- Work in human-directed phases. State the plan before writing code.
- **Stop after each phase** and deliver the report format the prompt
  requests. Do not start the next phase.
- Update `DECISIONS.md` for every real technical decision: choice +
  reason, concise. Do not rewrite existing entries.
- Never revert files the human or a formatter has modified.
- Do not add features, refactors, or "improvements" beyond the current
  phase's scope. When the prompt says "no new features," take it literally.

## Git rules

- **Never `git commit`. Never `git push`.** The human reviews and commits
  each phase. If asked to inspect history, that is read-only.
- The repo is expected to stay clean: no `bin/`, `obj/`, `node_modules/`,
  `dist/`, `.angular/`, `.vs/`, and no environment-specific appsettings in
  source control.

## Frontend rules

- Angular 21 standalone components, signals, zoneless change detection;
  functional `HttpInterceptorFn` / `CanActivateFn`.
- The client displays server state; it never computes status, thresholds,
  or permissions beyond hiding controls the server would refuse.
- Request payloads must match the backend contracts exactly (e.g.
  approval = `{ decision }` or `{ decision, reason }`).
- Tests use **Vitest, not Jasmine/Karma**:
  - no `spyOn` — use `import { vi } from 'vitest'` and `vi.spyOn(...)`;
  - no `.toBeTrue()` / `.toBeFalse()` — use `.toBe(true)` / `.toBe(false)`;
  - protected members need `as unknown as TestType` structural casts in
    specs;
  - Angular's `min` validator error shape is `{ min: { actual, min } }`.
- Angular CLI is pinned to 21.x on purpose (Node 22.16 engine constraints).
  Do not upgrade the CLI or TypeScript casually.

## Verification commands (verified working)

Run from the repository root unless stated otherwise:

```bash
# Backend build (expect 0 warnings, 0 errors)
dotnet build MaintenanceManagementSystem.slnx

# Backend tests — 98 tests; runs against in-memory SQLite, does NOT
# require SQL Server and does not touch the dev database
dotnet test MaintenanceManagementSystem.slnx

# Frontend build and tests
cd MaintenanceManagementSystem.Client
npm run build        # production build → dist/
npm test             # 22 unit tests (Vitest)

# Run the apps
dotnet run --project src/MaintenanceManagementSystem.Api   # http://localhost:5295
npm start                                                  # http://localhost:4200 (in client dir)
```

Backend test counts to expect: 98 passed, 0 failed. Frontend: 22 passed.
If your change legitimately adds tests, update these expectations in the
phase report — do not weaken tests to hit a number.

## Environment notes (Windows + bash)

- Tests are safe for the dev database (in-memory SQLite via `ApiFactory`),
  but any **manual** verification against the live API does mutate SQL
  Server — restore afterwards:
  `DELETE FROM AuditEntries; DELETE FROM MaintenanceRequests;`
  `DBCC CHECKIDENT('AuditEntries', RESEED, 0);`
  `DBCC CHECKIDENT('MaintenanceRequests', RESEED, 0);`
- The bash shell escapes `!` even inside single quotes — build JSON request
  payloads in temp files (with `printf`) instead of inline strings.
- `ng serve` may survive its shell wrapper on stop. Kill by PID:
  `netstat -ano | grep :<port>` then `taskkill //PID <pid> //F`.
- API runs at http://localhost:5295 (the Angular environment targets this
  exactly); client at http://localhost:4200. OpenAPI JSON (dev only):
  http://localhost:5295/openapi/v1.json — there is intentionally no
  Swagger UI.
