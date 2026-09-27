# MaintenanceManagementSystem — Engineering Decisions

Technical evaluation project for a facilities management company.
Multi-tenant maintenance request management with cost-based approval,
spend reporting, and an audit trail.

Stack: ASP.NET Core Web API (.NET 10, C#), EF Core, SQL Server, Angular (later phase), xUnit.

Each decision below lists the choice and the reason it was made. The guiding
constraint is a 4–6 hour technical evaluation: everything is deliberately
simple, reviewable, and testable.

---

## 1. Naming and solution layout

- **Decision:** The name `MaintenanceManagementSystem` is used for the
  solution, projects, namespaces, folders, database, and documentation.
  The working name `MaintServ` was dropped.
- **Reason:** Single consistent name avoids mapping between the working
  title and real artifacts.

## 2. Single backend project, no Clean Architecture / CQRS / DDD

- **Decision:** One ASP.NET Core Web API project
  (`MaintenanceManagementSystem.Api`) with folder separation
  (`Domain/`, `Data/`, `Services/`, `Auth/`, `Contracts/`, `Controllers/`).
  No separate class libraries, no MediatR, no repository-per-entity,
  no abstraction layers beyond what testing requires.
- **Reason:** For this scope, extra layers add ceremony without adding
  evaluation signal. Folder separation preserves testable seams
  (domain rules do not depend on EF or HTTP) and can be split into
  projects later without logic changes.

## 3. API-first development

- **Decision:** The backend is built and verified through Swagger/OpenAPI
  and Postman before any Angular work. The frontend is the last phase.
- **Reason:** The security properties under evaluation (tenant isolation,
  authorization, workflow integrity, audit) are server-side properties.

## 4. Tenancy model

- **Decision:** Single shared SQL Server database. Every tenant-owned
  entity carries an `OrganizationId` column, including a **denormalized**
  `OrganizationId` on `MaintenanceRequest` (also reachable via `Site`).
  Organization identity always comes from the authenticated user's
  server-side claims — never from client input. Tenant-owned queries are
  scoped by EF Core global query filters (added with the auth phase,
  which introduces `TenantContext`). Fetch-by-id re-validates tenant
  ownership (defense in depth against IDOR). Cross-tenant access is
  explicitly tested.
- **Reason:** Database-per-tenant is over-engineering here. The
  denormalized org column makes isolation a single-column predicate,
  immune to navigation-path mistakes and join-free to filter.

## 5. Roles

- **Decision:** One role per user (`Requester` or `Approver`).
  - All authenticated users may raise maintenance requests
    (Approvers included).
  - Only Approvers can approve or reject.
  - An Approver can never approve or reject their own request
    (enforced server-side in business logic, returns 403).
  - Requesters cannot reach approval endpoints (403 via authorization
    policy).
- **Reason:** Matches the stated requirements, including the meaningful
  interpretation of "Approvers cannot approve their own requests"
  (implying Approvers can create requests).

## 6. Lifecycle and terminology

- **Decision:** `Raised -> PendingApproval -> Approved / Rejected ->
  Completed`. No Draft/Submitted states. `Rejected` and `Completed` are
  terminal. All transitions pass through one explicit allowed-transition
  map; invalid transitions return 409.
- **Reason:** The task's own vocabulary is used where practical, and the
  extra states added no required behavior.

## 7. Approval threshold

- **Decision:** Per-organization `ApprovalThreshold` (decimal 18,2).
  - `EstimatedCost <= threshold` → **automatic approval** at creation.
  - `EstimatedCost > threshold` → `PendingApproval`, requires an Approver.
  - The automatic approval is recorded as an audit event
    (`AutoApproved`), so every path to `Approved` is audited uniformly.
  - The threshold rule runs whenever `EstimatedCost` is set (creation,
    or raiser edit while `PendingApproval`), so the rule exists in one
    code path.
  - Threshold updates are Approver-only and audited.
- **Reason:** Gives the threshold real workflow meaning and keeps one
  canonical rule.

## 8. Actual cost policy

- **Decision:** `ActualCost` is recorded by the raiser when completing a
  request. If actual cost later exceeds the estimated cost or the
  approval threshold, **no second approval workflow is triggered in v1**.
  Estimated-vs-actual variance is surfaced through spend reporting.
- **Reason:** A re-approval loop adds workflow complexity with no
  requirement behind it. Reporting exposes the variance instead.

## 9. Edit rules

- **Decision:** The raiser may edit a request only while it is
  `PendingApproval`; `Approved`, `Rejected`, and `Completed` requests
  cannot be edited. Editing `EstimatedCost` re-runs the threshold rule
  (so lowering a cost below the threshold can auto-approve it). Rejection
  requires a reason.
- **Reason:** Keeps the threshold decision consistent with the cost that
  was current at approval time, and gives the rule a single definition.

## 10. Audit trail

- **Decision:** Every state change and approval/rejection decision
  produces exactly one `AuditEntry` (who / what / when: `ActorUserId`,
  `Action`, `PreviousStatus`, `NewStatus`, `Details`, `TimestampUtc`)
  written in the **same transaction** as the business change. Audit
  records are append-only from the application perspective: write-only
  repository usage, no update/delete endpoints, no update/delete code
  paths. **No SQL trigger** unless a concrete requirement later justifies
  it. System auto-approvals are audited with the submitting user as the
  actor and noted as a system decision.
- **Reason:** Same-transaction writes make audit and state change
  atomic. Application-level append-only is sufficient for this
  evaluation and avoids database-specific surface; a DB-level guard can
  be added later if required.

## 11. Deletes

- **Decision:** No delete endpoints in v1 for sites or maintenance
  requests. All foreign keys use `ON DELETE RESTRICT`.
- **Reason:** Deleting tenant data undermines audit integrity; nothing
  requires delete behavior.

## 12. Authentication (planned phase 2)

- **Decision:** JWT bearer tokens carrying `sub` (user id), `org`
  (organization id), and `role` claims. Password hashing via the ASP.NET
  Core `PasswordHasher` (no full Identity stack). `[Authorize]`
  globally; anonymous access only for login.
- **Reason:** Standard, minimal, and everything the authorization rules
  need is in the token claims.

## 13. Secrets and configuration

- **Decision:** The SQL Server connection string is stored in
  `dotnet user-secrets` (per-user secret store) and is **not** in source
  control. The installed default SQL Server instance (`localhost`,
  Windows authentication) is used rather than LocalDB.
- **Reason:** Keeps credentials out of the repository with standard
  tooling.

  ```sh
  cd src/MaintenanceManagementSystem.Api
  dotnet user-secrets init
  dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=MaintenanceManagementSystem;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=false"
  ```

## 14. Migrations and seeding

- **Decision:** Code-first EF Core migrations. In Development, the API
  applies migrations and seeds demo data at startup; the seeder is
  idempotent (no-op when data exists). Seed data deliberately spans two
  organizations so tenant isolation can be demonstrated and tested.
- **Reason:** Startup migration/seed keeps local setup to `dotnet run`.
  Runtime seeding (rather than `HasData`) lets password hashes be
  generated properly at run time.

## 15. Data types

- **Decision:** Integer identity keys; `decimal(18,2)` for all money;
  enums stored as readable strings (`"Approver"`, `"PendingApproval"`);
  UTC datetime columns suffixed `Utc`.
- **Reason:** Readability for reviewers and straightforward queries;
  decimal avoids floating-point money errors.

## 16. Demo seed data (development only)

| Organization (threshold) | Sites | Users |
|---|---|---|
| Northgate Facilities (1000.00) | HQ Tower, Riverside Depot | alice.requester@northgate.example (Requester), bob.approver@northgate.example (Approver) |
| Summit Property Group (2500.00) | Summit Plaza | carol.requester@summit.example (Requester), dave.approver@summit.example (Approver) |

All demo users share the password `Pass123$` (development only).

## 17. Out of scope (v1)

Self-registration and user management UI, delete endpoints, refresh
tokens, background jobs, caching, messaging, and any infrastructure
beyond SQL Server + the API host.

---

# Phase 2 additions (authentication & authorization)

## 18. JWT authentication

- **Decision:** JWT bearer tokens containing claims `sub` (user id),
  `org` (organization id), `role`, `email`. Inbound claim mapping is
  disabled (`MapInboundClaims = false`) so claim names are identical on
  both sides of the token; `RoleClaimType` is explicitly `"role"`.
  Settings (`Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`,
  `Jwt:LifetimeMinutes`) are bound via the options pattern and validated
  at host startup (`ValidateOnStart`, key >= 32 chars, HS256).
  Development values live in user-secrets; the signing key is generated
  randomly and never committed. Token lifetime default: 60 minutes; no
  refresh tokens in v1. No user enumeration: unknown email and wrong
  password return the identical empty 401.
- **Reason:** Standard minimal bearer setup; explicit claim names avoid
  silent remapping surprises and keep TenantContext parsing simple.

## 19. Secure-by-default authorization

- **Decision:** The authorization **fallback policy** requires an
  authenticated user for every endpoint; only login, `/health`, and the
  dev-only OpenAPI document are `[AllowAnonymous]`. An `ApproverOnly`
  policy (`RequireRole("Approver")`) is registered now and will be
  applied to approver-only endpoints from phase 3 on.
- **Reason:** New endpoints are protected by default — forgetting
  `[Authorize]` is impossible rather than a vulnerability.

## 20. TenantContext

- **Decision:** A scoped `TenantContext` service resolves
  `UserId`/`OrganizationId`/`Role` from the validated JWT claims
  (`IHttpContextAccessor`) once per request. Application code must use
  it for all tenant scoping; EF Core global query filters (phase 3,
  decision 22) are built on top of it. Since phase 3 the identity
  properties are nullable: outside an authenticated request they are
  null (default-deny), and code that must have an identity calls
  `RequireOrganizationId()` / `RequireUserId()` / `RequireRole()`,
  which fail loudly instead of silently proceeding without a tenant.
- **Reason:** Single server-side source of tenant identity; client
  organization ids are never trusted.

## 21. Integration test setup

- **Decision:** Tests host the real `Program` via
  `WebApplicationFactory` with environment `Testing` (skipping the
  Development startup block), SQL Server replaced by in-memory SQLite
  (schema from the EF model), and JWT settings supplied in-memory.
  Since phase 3, ApproverOnly is additionally covered by HTTP-level 403
  integration tests against the real endpoints.
- **Reason:** Fast, self-contained integration tests over the real
  pipeline; SQLite is adequate while no SQL Server–specific SQL exists.

---

# Phase 3 additions (sites + organization threshold)

## 22. Tenant isolation via EF Core global query filters

- **Decision:** `AppDbContext` takes `TenantContext` as a scoped
  constructor dependency; `OnModelCreating` adds global query filters
  for `Organization` (`Id == tenant org`), `Site`,
  `MaintenanceRequest`, and `AuditEntry`
  (`OrganizationId == tenant org`). The filter reads the **current
  request's** tenant value at query-execution time, so the cached EF
  model remains correct across requests from different organizations.
  A null tenant identity (seeding, login, background work) makes the
  comparison match **zero rows** — default-deny; a missing identity can
  never produce an unfiltered, all-tenants query. Services additionally
  keep explicit org predicates on fetch-by-id queries (defense in
  depth). The `User` entity is intentionally unfiltered (login runs
  pre-authentication; no user endpoints in v1). The development seeder
  bypasses filters with `IgnoreQueryFilters()` because it runs with
  system privileges outside any request.
- **Reason:** Tenant isolation is enforced in the data layer by
  construction rather than by remembering a WHERE clause in every
  endpoint, while remaining simple and framework-free.

## 23. Cross-tenant id handling

- **Decision:** There is no `/api/organizations/{id}` route at all — an
  organization is only addressable as `current`. Site updates look up
  with `Id == requestedId AND OrganizationId == tenant org` and return
  **404** for other tenants' sites (identical to unknown ids), so
  existence of foreign data is never revealed. Create/update request
  DTOs contain **no `OrganizationId` property at all** — extra fields in
  request JSON are ignored by binding and the tenant is assigned
  server-side from `TenantContext`.
- **Reason:** IDOR-by-URL has no target; 404 avoids resource
  enumeration; DTO shape makes tenancy smuggling impossible.

## 24. No schema migration in phase 3

- **Decision:** No new migration. The phase required no model changes —
  `Sites.OrganizationId`, `Organizations.ApprovalThreshold`, and all
  indexes already exist from phase 1. Query filters affect queries, not
  schema.
- **Reason:** Avoids meaningless migration noise; the migration history
  stays truthful.
