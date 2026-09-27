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

---

# Phase 5 additions (approval, rejection, completion, editing)

## 32. Manual approval and rejection: one endpoint, resource rules in the service

- **Decision:** `POST /api/maintenance-requests/{id}/approvals` with
  `{ decision: "approve" | "reject", reason? }` (case-insensitive enum
  binding; anything else is a 400 at the boundary). The endpoint carries
  the `ApproverOnly` policy (a Requester gets 403 from authorization,
  before any business logic). The service enforces the resource rules:
  the approver must belong to the request's organization (tenant-scoped
  lookup — a foreign id is **404**, indistinguishable from unknown), must
  **not be the raiser** (**403** even with the Approver role), and the
  request must be `PendingApproval` (**409 Conflict** — auto-approved or
  already-decided requests cannot be decided again). Approval sets
  `ApprovedByUserId`, `ApprovedAtUtc`, clears `RejectionReason`; rejection
  keeps `ApprovedByUserId`/`ApprovedAtUtc` null and saves the reason.
- **Reason:** Role policy and resource ownership are different layers;
  splitting them keeps the endpoint gate cheap while the service remains
  the single authority over who may touch which request. 409 distinguishes
  "exists but wrong state" from 404 "does not exist for you" without
  leaking anything across tenants.

## 33. Rejection requires a reason

- **Decision:** The reason is mandatory and non-blank (checked at the
  boundary → 400) with a 1000-character maximum matching the entity's
  `RejectionReason` column, and is stored both on the request and in the
  audit entry's `Details`.
- **Reason:** A rejection without a stated reason defeats the purpose of
  the field; the column-length match removes silent truncation.

## 34. Completion belongs to the raiser alone; actual cost never re-runs approval

- **Decision:** `POST /api/maintenance-requests/{id}/completion` requires
  the caller to be the **original raiser** (403 for anyone else — the
  Approver role grants no completion rights over others' requests) and the
  request to be `Approved` (409 for PendingApproval/Rejected/Completed —
  terminal states are not repeatable). `ActualCost` is required, >= 0, and
  recorded with `CompletedAtUtc`; per decision 8, an actual cost above the
  estimate or the current threshold triggers **no second approval
  workflow** — the variance belongs to spend reporting. An auto-approved
  request is completable by its raiser exactly like a manually approved
  one.
- **Reason:** The raiser closes out their own work; separating "who
  completes" from roles keeps the rule about authorship, and keeping
  actual cost outside approval avoids a re-approval loop with no
  requirement behind it (decision 8).

## 35. Editing: raiser-only, PendingApproval-only, threshold re-evaluated on cost change

- **Decision:** `PUT /api/maintenance-requests/{id}` has exactly the
  create DTO's shape and validation (no workflow-owned fields exist to
  smuggle). Only the raiser may edit (an Approver has no special rights,
  403) and only while `PendingApproval` (409 once
  Approved/Rejected/Completed). The new `SiteId` must belong to the
  caller's organization (foreign/unknown → 404). If `EstimatedCost`
  **changes**, the organization's **current** threshold is re-evaluated:
  at or below it the request is auto-approved as a system decision
  (`ApprovedByUserId` null, system `AutoApproved` audit entry, actor null,
  threshold named in `Details`); above it the request stays
  `PendingApproval` with no audit noise. A cost-unchanged edit does not
  re-evaluate the threshold (no surprise auto-approval on a title-only
  edit). `PendingApproval` has no transition to `Rejected`/`Completed` in
  the lifecycle map, so an edit can never produce those states.
- **Reason:** The threshold rule must have one definition across creation
  and editing; evaluating only on real cost changes keeps edits
  predictable while still letting a corrected estimate resolve the queue.

## 36. Status-preserving edits are not audited

- **Decision:** Only state transitions and approval decisions produce
  audit entries (decision 10's scope). An edit that leaves the request in
  `PendingApproval` writes nothing; an edit that triggers auto-approval
  writes exactly the `AutoApproved` entry, whose `Details` name the new
  cost and threshold. There are no audit read/update/delete endpoints at
  all.
- **Reason:** Keeps the trail meaningful instead of noisy; the request row
  itself is the record of current field values, and every status path to
  `Approved` remains uniformly audited (decision 7).

## 37. No schema migration in phase 5

- **Decision:** No new migration. All workflow fields
  (`ApprovedByUserId`, `RejectionReason`, `ActualCost`, timestamps) and
  the terminal-state model existed from phase 1; phase 5 only exercises
  them.
- **Reason:** The migration history stays truthful.

---

# Phase 6 additions (read-only audit API)

## 38. Audit records are exposed through a single read-only, tenant-scoped endpoint

- **Decision:** `GET /api/audit?requestId=` is the only API surface for
  audit: authenticated (both roles — the fallback policy suffices; no
  Approver-only requirement), returning only the caller's organization's
  entries as projected `AuditEntryDto`s (never EF entities). Tenant
  isolation comes from the existing global query filter — no
  `IgnoreQueryFilters()` in application code — with the explicit
  organization predicate kept on the requestId path (decision 22). A
  foreign or unknown `requestId` returns **200 with an empty list**,
  indistinguishable, so other tenants' audit existence is never revealed.
  Results are ordered `TimestampUtc, then Id`. Writes remain possible only
  inside the business transaction via `AuditService.Stage`; there is no
  POST/PUT/PATCH/DELETE audit route, and `ActorUserId`/`ActorEmail` are
  null for system decisions (decision 26).
- **Reason:** The audit trail needs to be visible to the people it
  protects, but only through a read-only, org-bounded lens; extending the
  existing `AuditService` (rather than adding a second abstraction) keeps
  one choke point for both the write and the one sanctioned read.

## 39. No schema migration in phase 6

- **Decision:** No new migration. `AuditEntries` already carries every
  exposed field; the endpoint only reads it.
- **Reason:** The migration history stays truthful.

---

# Phase 7 additions (organization spend report)

## 40. Spend = ActualCost of Completed requests, dated by CompletedAtUtc, over an inclusive half-open UTC range

- **Decision:** `GET /api/reports/spend?from=&to=` reports actual money
  spent per site: only `Completed` requests contribute, and only their
  `ActualCost` (null ActualCost never contributes, even though the
  completion API always sets it — the null rule is defense in depth).
  The range applies to `CompletedAtUtc` as a half-open UTC interval —
  `[from 00:00:00Z, to+1day 00:00:00Z)` — so both boundary dates are
  fully inclusive without time-of-day string comparisons. `from` and
  `to` are required, must parse as dates, and `from > to` is a **400**
  (never silently swapped); there is no organization parameter — the
  organization is always the caller's. Sites with no qualifying spend in
  the range are omitted (no zero-spend rows), and rows are ordered by
  `SiteId` for deterministic output.
- **Reason:** "Spend" must mean one thing across the system (decision 8's
  variance philosophy), the half-open interval makes boundary semantics
  exact, and rejecting (rather than fixing) an inverted range keeps the
  report's meaning unambiguous.

## 41. The aggregation runs entirely in the database; tenant isolation is layered

- **Decision:** `ReportService.SpendBySiteAsync` issues a single SQL
  query — filtering, per-site summing, zero-spend elimination, and
  ordering all execute in the database; no request rows are ever loaded
  into memory for the report and no caching is applied. EF Core 10 cannot
  translate a `GROUP BY` whose key or surrounding join touches the
  query-filtered `Site` side, so the aggregation is rooted at `Site` with
  a filtered `SUM` subquery over its request navigation — semantically
  identical grouping, fully translatable. Tenant isolation uses the same
  layers as everywhere else (decision 22): the global query filters on
  `Site` and `MaintenanceRequest` plus an explicit
  `Site.OrganizationId == tenant organization` predicate. A foreign
  tenant simply sees only its own sites' spend — or an empty list — and
  no response row carries an `OrganizationId`. No schema migration:
  phase 1 already has every column the report reads.
- **Reason:** Reports must not become an accidental in-memory table scan
  or a cross-tenant leak; rooting the query at the (already
  org-filtered) site keeps both the translation working and the
  isolation guarantees identical to the rest of the system.

---

# Phase 8 additions (security & test hardening)

## 42. Whitespace-only text is rejected by a dedicated validation attribute; hardening audit changes nothing else

- **Decision:** The phase-8 audit (tenant isolation, authorization,
  validation, audit integrity, lifecycle, secrets/config) confirmed the
  existing layered defenses everywhere else, so the only code change is a
  validation tightening: request titles/descriptions and site names reject
  whitespace-only values via a small custom `[NotWhitespace]` attribute
  (empty/whitespace after trimming → 400), because the services trim these
  fields and `"   "` would otherwise be stored as an empty value. It is
  deliberately **not** implemented with `RegularExpressionAttribute`:
  that attribute matches the **whole** string against the pattern
  (implicit anchoring — `\S` would mean "exactly one non-whitespace
  character and nothing else"), so the "contains at least one
  non-whitespace character" rule would need an unreadable look-ahead
  pattern. New focused tests pin the remaining invariants: identity/role
  smuggling in the login body is ignored, missing completion
  `actualCost` is a 400, and the `RequestLifecycle` map itself (allowed
  transitions, terminal states, throw-on-illegal) is unit-tested.
- **Reason:** Prefer fixing the one genuine gap with the smallest readable
  change and pinning already-good invariants with tests, over adding
  frameworks, repositories, or exhaustive coverage the task never asked
  for.

---

# Phase 9.1 additions (Angular client foundation & authentication)

## 43. Angular 21 client: minimal signal-based auth, no frontend security

- **Decision:** `MaintenanceManagementSystem.Client` is Angular 21
  (standalone, signals, zoneless) — CLI pinned to major 21 because the
  installed Node 22.16.0 is below Angular 22's engine floor; npm's
  arborist crash on the local npm 10.9.2 was worked around by installing
  with `npx npm@11` (no global tooling changed). Auth is deliberately
  minimal: JWT persisted in `localStorage`, a signal-backed
  `AuthService` as the single source of state, a functional interceptor
  (Bearer attach + 401 → clear session, redirect to `/login` with
  `returnUrl`), and a `CanActivateFn` guard returning a `UrlTree`. On
  startup the shell calls `/api/auth/me` to validate a persisted token.
  Logout is client-side only (stateless JWT; no server endpoint).
  Backend change is one CORS policy (`AngularDevClient`, origin
  `http://localhost:4200`, applied before authentication so preflight
  OPTIONS is anonymous).
- **Reason:** The backend remains the security authority; the guard and
  interceptor are UX affordances only. Anything richer (refresh tokens,
  NgRx, UI kits) was explicitly out of scope for this phase.

## 44. API base URL is an environment constant, not a build-time secret

- **Decision:** The client reads `environment.apiBaseUrl`
  (`http://localhost:5295/api`) and the interceptor only attaches the
  Authorization header to requests under that prefix. No secrets live in
  the client or its repository files.
- **Reason:** Keeps one obvious place to swap for a reverse-proxied
  relative path later, and prevents leaking the token to third-party
  origins if external URLs are ever requested.

## 45. Request list/create stay thin: the server's status is displayed, never computed

- **Decision:** The phase 9.2 client screens (request list, create form)
  contain no workflow logic: the create payload is exactly the backend's
  `CreateMaintenanceRequestRequest` (siteId, title, description,
  estimatedCost — no organization/user/status fields), and after creation
  the UI shows the status the API returned. Form validation mirrors only
  the DTO's boundary rules (required, lengths, whitespace-only rejection,
  cost >= 0), not business rules; the approval threshold is never
  evaluated client-side.
- **Reason:** Any client-side threshold or status computation would be a
  second source of truth that can drift from the backend. Verified
  server behavior: 250.00 auto-approves (system decision,
  approvedByUserId null), 1500.00 stays PendingApproval (Northgate
  threshold 1000), while 2400.00 auto-approves under Summit's 2500
  threshold — per-organization decisions the frontend merely renders.



