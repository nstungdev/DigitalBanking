---
name: review-code-be
description: >
  Reviews backend code changes against DDD conventions, naming clarity, latent bugs, cyclomatic
  complexity (max 10), and performance. Use this skill whenever:
  - The user asks to review backend/domain code ("review code", "review giúp tôi", "check code
    backend này", "review PR/branch này")
  - The user references a diff/PR/branch/file path that touches backend service code (Domain,
    Application, Infrastructure, Api layers)
  Do NOT use for frontend/Angular-only changes — this skill is backend-specific.
  Always reports findings via the ReportFindings tool, ranked most-severe first (empty list if
  nothing survives verification) — never print findings as plain text instead.
  Reply in the same language the user is chatting in: if their messages are in Vietnamese,
  write the whole review (narration + findings' free-text fields) in Vietnamese; otherwise
  default to English. Decide this fresh each run from the current conversation, don't assume.
---

# Review Code BE (DDD-focused)

Reviews backend code against 5 fixed dimensions: **DDD adherence, naming clarity, latent bugs,
cyclomatic complexity, and performance**. Built for services following Domain-Driven Design
(entities/aggregates, value objects, domain events, layered Domain → Application/Infrastructure
→ Api) — the checklists below are framework-agnostic but examples lean C#/.NET since that's
this repo's stack.

## Step 0 — Determine scope

- No target given → review the current git diff: `git diff` (unstaged) + `git diff --staged`;
  if both are empty, diff the current branch against its base (`git diff origin/master...HEAD`
  or the repo's actual default branch).
- User gives a PR number / branch name / file path(s) → review that instead.
- Only look at **backend** files (`.cs`, `.sql`, backend `.csproj`/config touching the reviewed
  logic). Skip Angular/`.ts`/`.html` files even if they appear in the same diff — flag that
  they were out of scope rather than silently reviewing them.

If the scope is empty (no backend changes found), say so and stop — don't invent findings.

## Step 1 — Review across the 5 dimensions

Read every changed file in full (not just the diff hunks) when you need surrounding context to
judge correctness — a 3-line diff can hide a bug that only shows up next to the rest of the
method.

### 1. DDD adherence
- **Encapsulation/invariants**: business rules enforced *inside* entity/aggregate methods, not
  via public setters or logic sitting in a service/handler that mutates state directly. Flag
  any `set;` on a property that represents a protected invariant.
- **Anemic domain model**: domain classes that are pure data bags (only get/set) while all
  behavior lives in "service"/"manager" classes — this is a DDD violation, not just a style
  nit.
- **Aggregate boundaries**: one aggregate root modified per transaction; no code reaching into
  another aggregate's internal entities directly instead of going through its root. Cross-
  aggregate consistency should go through domain/integration events, not direct manipulation.
- **Value Objects vs primitive obsession**: domain concepts like money, email, identifiers
  represented as raw `string`/`decimal` instead of a small immutable Value Object that
  validates itself in its constructor.
- **Ubiquitous language**: type/method/property names should use the bounded context's actual
  domain vocabulary (`Debit`, `Approve`, `Freeze`) instead of generic CRUD verbs (`Update`,
  `Process`, `Handle`) when a precise domain term exists.
- **Domain events**: raised from within the aggregate at the point of the state change they
  describe, not constructed after the fact in the application/infrastructure layer from
  inferred state.
- **Layering direction**: Domain layer must never depend on Infrastructure or Api (no EF Core,
  ASP.NET Core, or Wolverine-HTTP-specific types inside Domain classes). Flag violations as
  correctness issues, not style.

### 2. Naming clarity
- Intention-revealing names — flag `data`, `temp`, `obj`, `x1`, `info`, catch-all `Helper`/
  `Manager` names that don't say what the thing actually is.
- Booleans read as predicates (`IsActive`, `HasSufficientFunds`), not bare adjectives/flags.
- Method names are verbs that match what the method actually does — a `GetX` that has side
  effects, or a `Save` that also sends a notification, is a naming bug, not just unclear.
- Same domain concept named consistently across files (don't mix `Client`/`Customer` for the
  same thing).

### 3. Bug tiềm ẩn (latent bugs)
- Null-reference risk on external input (deserialized DTOs, query results) without a check.
- Off-by-one / boundary conditions in loops, ranges, pagination.
- Race conditions: shared mutable state, non-atomic check-then-act, missing synchronization.
- Async misuse: missing `await` ("fire and forget" that wasn't meant to be), `async void`
  outside event handlers, blocking on async code via `.Result`/`.Wait()` (deadlock risk).
- Resource leaks: `IDisposable` not wrapped in `using`/`await using`.
- Exception handling: swallowed exceptions (empty `catch`), catching `Exception` broadly where
  a specific type is expected, exceptions used for normal control flow.
- `float`/`double` used for money instead of `decimal`.
- Mutable state in `static`/singleton-scoped fields that isn't thread-safe.

### 4. Độ phức tạp thấp — cyclomatic complexity ≤ 10
- Estimate cyclomatic complexity per method: start at 1, add 1 for each `if`, `else if`,
  `case`, `catch`, `for`/`foreach`/`while`, and each `&&`/`||` in a condition.
- Flag any method estimably **over 10**. Report the estimated number in the finding.
- Also flag deep nesting (>3 levels) as a softer signal even under 10 — usually fixable with
  guard clauses/early return.
- When flagging, suggest the concrete fix (extract method, guard clause, replace nested
  conditionals with polymorphism/strategy for domain branching logic) — not just "too complex".

### 5. Hiệu suất (performance)
- N+1 query patterns: querying inside a loop over a previously loaded collection.
- Missing `AsNoTracking()` on read-only EF Core queries.
- Loading full entities when only a projection/DTO's worth of columns is needed.
- Synchronous I/O on a request path that should be async (blocking calls, `Task.Run` used to
  fake async over sync work).
- Unbounded queries/collections returned without pagination.
- Needless allocations in hot paths (re-enumerating LINQ, string concatenation in a loop
  instead of `StringBuilder`).
- Query patterns implying a missing index (e.g. `WHERE`/`JOIN` on a column that isn't the PK
  and doesn't look indexed) — mark these `PLAUSIBLE` not `CONFIRMED` since schema isn't visible
  from code alone.

## Step 2 — Verify before reporting

For each candidate finding, sanity-check it against the actual surrounding code before keeping
it — don't report a "bug" that's actually handled a few lines away, and don't flag a
complexity/performance issue without pointing at the real cause. Drop anything that doesn't
survive this check; do not pad the list to look thorough.

## Step 3 — Report

Call `ReportFindings` once with the surviving findings, most-severe first (empty array if none
survive). Use `category` values `ddd`, `naming`, `bug`, `complexity`, or `performance`. Set
`verdict: CONFIRMED` when you're certain, `PLAUSIBLE` when reasonable but unverifiable from
code alone (e.g. the missing-index case above). Do not also print the findings as prose —
`ReportFindings` is the output.

## Language

Match the user's language for every free-text field (narration before the tool call, finding
summaries) based on the language of their most recent message in this conversation — Vietnamese
in, Vietnamese out; otherwise English. Re-check this every run rather than assuming from a
previous session.
