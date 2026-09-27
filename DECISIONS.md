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
  - Threshold updates are Approver-only. They are **not** audited —
    `AuditEntry` is request-specific (see decisions 10 and 31): a
    threshold change is administrative configuration, not a request
    state change or approval decision.
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

- **Decision:** Every maintenance-request state change and
  approval/rejection decision produces exactly one `AuditEntry` (who /
  what / when: `ActorUserId`, `Action`, `PreviousStatus`, `NewStatus`,
  `Details`, `TimestampUtc`) written in the **same transaction** as the
  business change. The audit scope in v1 is **request-specific**: request
  lifecycle state changes and approval/rejection decisions (including
  system auto-approvals). Organization configuration changes — such as
  `ApprovalThreshold` updates — are administrative changes, not request
  state changes, and produce no `AuditEntry` (decision 31). Audit
  records are append-only from the application perspective: write-only
  repository usage, no update/delete endpoints, no update/delete code
  paths. **No SQL trigger** unless a concrete requirement later justifies
  it. System auto-approvals are audited with a **null actor** as the
  reserved marker for system-made decisions (decision 26).
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

---

# Phase 4 additions (requests, threshold auto-approval, audit)

## 25. Threshold rule is boundary-inclusive and evaluated at creation from the database

- **Decision:** At creation the service loads the organization's
  `ApprovalThreshold` from the database (never from the client) and applies:
  `EstimatedCost <= threshold` -> **Approved** (auto), `EstimatedCost >
  threshold` -> **PendingApproval**. Equality is deliberately inclusive.
  `ActualCost` (recorded later at completion) never re-runs this rule — a
  finished job whose actual spend exceeded the threshold does not go back
  through approval (v1 policy, decided at kickoff).
- **Reason:** The threshold is an organization setting that can change;
  deciding from the stored value keeps the rule consistent, and a single
  evaluation point (creation) keeps the workflow simple and auditable.

## 26. Auto-approval is represented with no approver and an explicit system audit entry

- **Decision:** An auto-approved request keeps `ApprovedByUserId = null`
  (no human decided it — this also keeps "approvers never approve their own
  requests" trivially true for auto-approvals) and `ApprovedAtUtc` set. The
  audit trail records two entries: `RequestRaised` (actor = creator,
  `null -> Raised`) and `AutoApproved` (**actor = null**, `Raised ->
  Approved`, details naming the cost and threshold). Entering the queue is
  recorded as `SubmittedForApproval` (actor = creator, `Raised ->
  PendingApproval`). `null` as audit actor is the reserved marker for
  system-made decisions.
- **Reason:** Reporting must be able to distinguish "decided by nobody
  (system rule)" from "decided by a person"; overloading a real approver id
  would corrupt that distinction and the self-approval invariant.

## 27. Status changes go through an explicit transition map

- **Decision:** `RequestLifecycle` holds an explicit
  from-status -> allowed-to-status map, and `ApplyTransition` is the only
  way code changes `MaintenanceRequest.Status`; a transition not in the map
  throws. The map currently contains the two creation-time transitions
  (`Raised -> PendingApproval`, `Raised -> Approved`) and is extended as
  manual approval/rejection and completion are implemented in later phases.
- **Reason:** The agreed lifecycle stays machine-checked instead of
  scattered across if-statements; illegal states (e.g. `Rejected ->
  Approved`) become unrepresentable in application code.

## 28. Audit rows are staged through one service and committed in the caller's transaction

- **Decision:** `AuditService` is the single choke point for writing audit
  entries: it only stages (`DbSet.Add`) rows onto the caller's
  `DbContext`/`SaveChangesAsync`, copying `OrganizationId` from the request
  and linking via the `MaintenanceRequest` navigation so EF resolves the FK
  for not-yet-saved requests. There is no read, update, or delete path —
  audit is append-only (no API, no service method). One `SaveChangesAsync`
  in `MaintenanceRequestService.CreateAsync` commits the request and both
  audit rows atomically. No SQL triggers are used.
- **Reason:** Same-transaction audit cannot drift from the business change
  (decision 10), and a single write choke point makes "append-only"
  enforceable rather than aspirational.

## 29. Request creation validates site ownership; responses carry no tenancy fields

- **Decision:** `CreateMaintenanceRequestRequest` contains only `SiteId`,
  `Title`, `Description`, `EstimatedCost` (all validated: SiteId >= 1,
  Title <= 200, Description <= 2000, EstimatedCost >= 0, matching the EF
  column limits). The service resolves the site with
  `SiteId == requested AND OrganizationId == tenant org`; miss = **404**,
  identical to an unknown site. The response DTO exposes site name and
  raiser email for readability but no `OrganizationId` — every row in every
  response belongs to the caller's organization by construction.
- **Reason:** Same IDOR/enumeration protections as sites (decision 23),
  applied to the request resource; the DTO shape prevents tenancy smuggling
  at the binding layer.

## 30. No schema migration in phase 4

- **Decision:** No new migration. `MaintenanceRequests` and `AuditEntries`
  were fully modeled in phase 1; phase 4 only populates them.
- **Reason:** The migration history stays truthful — schema exists only
  where the model actually changed.

## 31. Audit scope is request-specific — threshold changes are not audited

- **Decision:** `AuditEntry` is intentionally maintenance-request-specific
  in v1: it records request lifecycle state changes and
  approval/rejection decisions (including system auto-approvals), and
  every row is attached to a `MaintenanceRequestId`. Changing an
  organization's `ApprovalThreshold` is an administrative configuration
  change, not a maintenance-request state change or approval decision,
  so it produces no `AuditEntry`. This corrects the phase-1 wording
  "threshold updates are … audited" (decision 7), which was overly broad.
  If configuration-change auditing is ever required, it needs its own
  mechanism (e.g. a general audit log) rather than a widening of
  `AuditEntry`. No schema, API, or code change accompanies this — it is a
  documentation-only scope clarification made during the phase-4 review.
- **Reason:** The task requires auditing request lifecycle changes and
  approval decisions; keeping `AuditEntry` request-specific matches that
  requirement without inventing a second audit subsystem the evaluation
  does not ask for.
