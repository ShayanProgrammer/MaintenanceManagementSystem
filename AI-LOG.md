# AI-LOG.md — How AI Was Used

This project was built with an AI agent (Claude, via Claude Code) as the
implementation engine, directed and reviewed by the human project owner.
This log describes honestly how that worked, what was delegated, where it
went wrong, and how mistakes were caught. Everything below is taken from the
actual development record; nothing is invented.

## Working model

The development loop for every phase was:

1. **Human decides and instructs.** The owner defined each phase's scope,
   constraints, and required verification in a written prompt.
2. **AI implements.** The agent read the existing code and documentation,
   stated a plan, then wrote the production code, tests, and documentation
   updates.
3. **AI verifies.** Builds and test suites were run, and behavior was
   verified manually against the live API (login, cross-tenant probes,
   threshold boundaries, lifecycle transitions, error codes).
4. **Human reviews and commits.** The agent was forbidden from committing or
   pushing. Every one of the incremental per-phase commits was made by the
   human after reviewing the phase report. The agent stopped after each
   phase and waited.

## What was delegated to AI

- The full backend implementation across phases: schema/migrations, seed
  data, JWT authentication, tenant isolation, sites/organizations,
  maintenance requests and lifecycle, threshold-based approval, audit
  trail, spend reporting, and a security/test-hardening pass.
- The Angular client: authentication (login, token storage, interceptor,
  route guard), request list/create, and the approver approve/reject
  workflow.
- Test authoring: 98 backend integration tests and 22 frontend unit tests.
- Debugging, root-cause analysis, and manual verification sequences.
- Drafts of `DECISIONS.md` entries (the human reviewed them; two decisions
  are corrections the human requested to AI-drafted scopes).

## Where constraints were tight

The prompts enforced non-negotiable constraints the AI had to work within:

- **Single backend project.** No Clean Architecture, CQRS, MediatR, or
  repository layers — "no abstraction layers beyond what testing requires."
- **Server-authoritative security.** Organization/role identity only ever
  from the JWT; client DTOs may not carry it; the client displays status
  but never computes it.
- **No scope creep.** During hardening (Phase 8) explicitly: "Do not add
  new business features or redesign the architecture."
- **Exact contracts.** Approval requests are exactly
  `{ decision }` or `{ decision, reason }`; no server-owned fields from
  the client.
- **Process discipline.** Explain the plan before coding; stop after each
  phase; update `DECISIONS.md` concisely; never revert human-modified
  files; never commit or push.

## What stayed manual

- Every git commit and the push to the remote — done by the human.
- Phase acceptance: the human read each phase report and decided whether to
  proceed.
- Decisions about scope, ambiguity resolution, and what would or would not
  be built (recorded in `DECISIONS.md`).

## A real prompt (excerpt)

The following is a verbatim excerpt of the Phase 8 (Security & Test
Hardening) prompt, shortened with ellipses; wording is unchanged:

> We are now starting Phase 8: Security & Test Hardening.
>
> IMPORTANT:
> Do not add new business features or redesign the architecture.
> Do not introduce Clean Architecture, CQRS, MediatR, repositories,
> background jobs, caching, or other unnecessary abstractions.
> The goal is to audit and harden the implementation we already have.
>
> Read the current codebase, DECISIONS.md, tests, and task requirements
> before making changes.
>
> Phase 8 goals:
>
> 1. TENANT ISOLATION AUDIT
>    Review every authenticated endpoint and every tenant-owned
>    entity/query. [...]
>
> 2. AUTHORIZATION AUDIT
>    Review every endpoint and verify server-side authorization. [...]
>
> 3. INPUT VALIDATION AUDIT
>    Review request DTOs and service boundaries. [...]
>
> 4. AUDIT INTEGRITY [...]
> 5. LIFECYCLE HARDENING [...]
> 6. SECRETS / CONFIGURATION
>    [...] Do NOT expose or print any secret values in your response. [...]
> 7. TEST STRATEGY
>    [...] The task explicitly says a few meaningful tests are preferred
>    over exhaustive coverage. [...]
> 8. RUN VERIFICATION
>    After any changes: Run the full test suite. Run the solution build
>    with 0 warnings/errors if possible. [...]
> 9. DOCUMENTATION
>    [...] Do not rewrite the whole document.
>
> FINAL RESPONSE FORMAT:
> Report: 1. What you audited. 2. What genuine issues you found.
> 3. What you changed. 4. What you deliberately did NOT change and why.
> 5. Tests added. 6. Final test count/result. 7. Build result.
> 8. Whether a migration is required. 9. Any remaining security concerns.
> 10. Files changed.
>
> Do not commit or push anything yet.
>
> Remember: this is a technical evaluation. Prefer simple, understandable,
> defensible code over maximum abstraction.

## A plausible-but-wrong output, and how it was caught

**What the AI produced.** During Phase 8, the input-validation audit found
that whitespace-only titles/descriptions/site names passed validation
(`[Required]` and `[MinLength(1)]` both accept `"   "`). The AI's first fix
used a built-in attribute that *sounds* exactly right:

```csharp
[RegularExpression(@"\S")]
```

reading `\S` as "must contain a non-whitespace character."

**How it was detected.** The full test suite was run immediately after the
change, as the workflow requires. It dropped from 98/98 passing to **36/98
passing — 62 failures**, spread across apparently unrelated tests: every
normal multi-word title, description, and site name was suddenly rejected
with a 400.

**Root cause.** `RegularExpressionAttribute` performs a **whole-string**
match (the pattern is implicitly anchored). `@"\S"` therefore means "the
entire value is exactly one non-whitespace character" — any normal string
fails. The regex was correct as a character-class description and wrong as
a whole-string pattern.

**Why it was easy to miss.**

- The intent-to-regex gap is subtle: most readers parse `\S` as
  "contains a non-whitespace character," which is exactly the requirement.
- 36 tests still passed, which falsely suggested a small, localized problem
  rather than a systemic one.
- The failures surfaced as generic model-validation 400s at the API
  boundary, far from the attribute that caused them.
- It was introduced *while fixing* validation, so it arrived disguised as a
  fix rather than as a risk.

**The correction.** Verified the matching semantics empirically, then
replaced the attribute with a purpose-built
`Validation/NotWhitespaceAttribute` (ignores null, rejects values that are
blank after trimming, clear error message) applied to request
`Title`/`Description` and site `Name`. The suite returned to **98/98** (run
twice to confirm). The episode is recorded as decision 42 in
`DECISIONS.md`.

**Lesson.** For framework-provided validation attributes, confirm the
matching/validation semantics before trusting intuition — a plausible
one-liner can invalidate half a system. Running the *entire* suite after
every small change is what turned this into a one-edit fix instead of a
shipped regression.

## A second genuine example: EF Core query translation (Phase 7)

The spend report needs per-site aggregated spend as a single DB-side query.
The AI's first query shape — `GroupBy` on `{ SiteId, Site.Name }` — failed
EF Core translation: a group key mixing an entity column with a navigation
over the query-filtered `Site` join does not translate, and EF inlines
scalar pre-projections back into that join. Two intermediate shapes (a join
after aggregation; a correlated name lookup inside the `GroupBy`
projection) also failed. The final shape roots the aggregation at `Site`
with a filtered `SUM` subquery over its request navigation — verified in
the SQL log as a single command with aggregation, zero-spend filtering, and
ordering all in the database, and both tenant-isolation layers present.

**Detection:** the integration tests threw translation exceptions on first
run — the suite again caught the problem immediately.

**Lesson:** with query-filtered entities, the "canonical" LINQ grouping
shape may not translate; each rejected alternative looked canonical, and
the invisible participant was the global query filter on `Site`.

## Smaller genuine tooling incidents (Phase 9.1)

- Angular CLI 22 refused to run on the installed Node (22.16.0) engine —
  resolved by pinning `@angular/cli` 21.x in the client.
- `npm install` under npm 10.9.2 crashed inside arborist (`edgesOut`) —
  resolved by installing with `npx -y npm@11 install`.
- A generated HTTP interceptor initially referenced an environment property
  it did not import — caught by the TypeScript build, fixed by importing the
  environment file.

## Honest summary

- The AI wrote essentially all of the code and tests; the human owned every
  scope decision, every commit, and final acceptance.
- The AI produced at least one confidently plausible but wrong solution
  (the regex above); the required verification workflow caught it within a
  single edit cycle.
- The strongest safeguard was not smarter prompting — it was a large,
  security-focused test suite plus mandatory full-suite runs after every
  change, enforced by the phase workflow.
