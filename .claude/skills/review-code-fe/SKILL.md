---
name: review-code-fe
description: >
  Reviews Angular frontend code changes against component/architecture conventions, naming
  clarity, latent bugs, cyclomatic complexity (max 10), and performance. Use this skill
  whenever:
  - The user asks to review frontend/Angular code ("review code FE", "review giúp tôi phần
    Angular", "check code trong src/Web", "review PR/branch này" when it touches src/Web)
  - The user references a diff/PR/branch/file path under src/Web (components, services, routes)
  Do NOT use for backend/.NET-only changes — use review-code-be for those instead.
  Always reports findings via the ReportFindings tool, ranked most-severe first (empty list if
  nothing survives verification) — never print findings as plain text instead.
  Reply in the same language the user is chatting in: if their messages are in Vietnamese,
  write the whole review (narration + findings' free-text fields) in Vietnamese; otherwise
  default to English. Decide this fresh each run from the current conversation, don't assume.
---

# Review Code FE (Angular-focused)

Reviews Angular frontend code against 5 fixed dimensions: **architecture/component
conventions, naming clarity, latent bugs, cyclomatic complexity, and performance**. Tuned for
this repo's stack specifically: Angular 22, standalone components (no NgModules), signals,
**zoneless change detection** (default since Angular 21), Nx workspace under `src/Web`, and
OIDC auth via `angular-auth-oidc-client` calling a single Gateway API — flag anything that
fights those defaults (e.g. code that only works because Zone.js patches something).

## Step 0 — Determine scope

- No target given → review the current git diff: `git diff` (unstaged) + `git diff --staged`;
  if both are empty, diff the current branch against its base.
- User gives a PR number / branch name / file path(s) → review that instead.
- Only look at **frontend** files under `src/Web` (`.ts`, `.html`, `.scss`/`.css`, Angular
  config like `project.json`/`tsconfig*.json`). Skip backend `.cs` files even if they appear in
  the same diff — flag that they were out of scope rather than silently reviewing them.

If the scope is empty (no frontend changes found), say so and stop — don't invent findings.

## Step 1 — Review across the 5 dimensions

Read every changed file in full (component + its template + its spec if present), not just the
diff hunks — a template binding only makes sense next to the component class that feeds it.

### 1. Architecture / component conventions
- **Standalone-first**: no `NgModule`-based components/directives/pipes introduced — everything
  standalone, matching this workspace's convention.
- **Signals over manual state**: component state that changes over time should be a `signal()`/
  `computed()`, not a plain class field mutated ad hoc with manual `markForCheck()` calls to
  compensate. `computed()` used for derived values instead of recomputing in the template or in
  multiple places.
- **Zoneless-safe reactivity**: anything that only updates the view because Zone.js used to
  patch it (raw `setTimeout`/`addEventListener`/`postMessage` callback mutating state without
  going through a signal or `ChangeDetectorRef`) is a real bug in this workspace, not a style
  nit — zoneless won't pick it up.
- **Smart/dumb separation**: presentational components should take `input()`/emit `output()`
  and stay free of HTTP/service calls; container components own data-fetching and pass data
  down. Flag presentational components reaching into services directly.
- **DI scope**: services scoped correctly (`providedIn: 'root'` for app-wide singletons vs.
  component-level `providers` for per-instance state) — flag app-wide state accidentally scoped
  to a component, or per-request state accidentally made a global singleton.
- **API access via Gateway only**: HTTP calls must go through the single Gateway base URL per
  this project's architecture — flag any call hardcoding a direct microservice URL/port.
- **Lazy loading**: feature routes should be lazy-loaded (`loadComponent`/`loadChildren`), not
  eagerly imported into the root route config.
- **No direct DOM manipulation**: flag raw `document.querySelector`/`ElementRef.nativeElement`
  mutation where a template binding or `Renderer2` would do.

### 2. Naming clarity
- Component/directive/pipe selectors and file names follow Angular's kebab-case convention and
  describe what the thing is, not `Helper`/`Utils`/`Data` catch-alls.
- Observables end in `$` (`user$`), signals don't (`user`) — flag inconsistent use since it's
  the convention this codebase should rely on to tell the two apart at a glance.
- Boolean signals/inputs read as predicates (`isLoading`, `hasError`), not bare nouns/flags.
- Event handler/output names describe the domain action (`accountOpened`), not generic
  (`onClick`, `changed`) when a more precise domain term exists.

### 3. Bug tiềm ẩn (latent bugs)
- **Subscription leaks**: manual `.subscribe()` without `takeUntilDestroyed()` or the `async`
  pipe — flag any subscription that outlives the component without an unsubscribe path.
- **Signal mutation**: mutating an object/array held in a signal in place (`sig().push(x)`)
  instead of `set()`/`update()` with a new reference — this silently breaks change detection.
- **`effect()` misuse**: side effects that write to the same signal(s) they read (risk of
  infinite loop), or `effect()` used to do something a `computed()` should do instead.
- **XSS risk**: `[innerHTML]` bound to unsanitized data, or `bypassSecurityTrust*` used without
  a clear, narrow justification.
- **Auth/token handling**: access/refresh tokens read from or written to `localStorage`
  directly instead of going through `angular-auth-oidc-client`'s own state — flag any code that
  bypasses the library to touch tokens itself.
- **Unguarded input**: `@Input()`/`input()` or route params used without a null/undefined check
  where the type allows it, or a non-null assertion (`!`) used to paper over a real "not loaded
  yet" state.
- **HTTP error handling**: HTTP calls with no `catchError`/error path — an error silently
  produces a stuck loading state or an unhandled rejection instead of user-visible feedback.
- **Race conditions**: overlapping in-flight requests for the same resource with no
  cancellation (e.g. fast typing into a search box firing requests that can resolve out of
  order) — flag missing `switchMap`-style cancellation.

### 4. Độ phức tạp thấp — cyclomatic complexity ≤ 10
- Estimate cyclomatic complexity per method/function the same way as backend: start at 1, add 1
  per `if`/`else if`/`case`/`catch`/loop/`&&`/`||`. Flag anything estimably **over 10**, with
  the estimated number in the finding.
- **Template complexity counts too**: deeply nested `@if`/`@for`/`*ngIf`/`*ngFor`, or non-
  trivial boolean/arithmetic expressions written inline in the template, are a complexity smell
  even though they're not "a method" — the fix is almost always a `computed()` signal or a
  small helper method the template just calls.
- Suggest the concrete fix (extract method, guard clause, move template logic to a
  `computed()`) rather than just reporting the number.

### 5. Hiệu suất (performance)
- **Zoneless-relevant re-renders**: work happening every change-detection cycle that should be
  memoized — e.g. a function call directly in a template binding (`{{ formatDate(x) }}`)
  instead of a `computed()`/pure pipe, recomputing on every CD run instead of once when inputs
  change.
- **Missing `track` in `@for`**: control-flow `@for` loops without a `track` expression (or
  legacy `*ngFor` without `trackBy`) over lists that can reorder/update — causes needless DOM
  recreation.
- **Bundle size**: importing an entire library for one function, or eagerly importing a feature
  module's worth of code into the root bundle instead of lazy-loading its route.
- **Images**: `<img>` for meaningful content without `NgOptimizedImage`/lazy loading.
- **Duplicate/uncached requests**: the same GET re-fired on every component init instead of
  being cached/shared (e.g. via a signal-backed store or `shareReplay`).

## Step 2 — Verify before reporting

For each candidate finding, sanity-check it against the actual surrounding code (component +
template + parent) before keeping it — don't report a "leak" that's actually cleaned up via the
`async` pipe a few lines away, and don't flag a performance issue without pointing at the real
cause. Drop anything that doesn't survive this check; do not pad the list to look thorough.

## Step 3 — Report

Call `ReportFindings` once with the surviving findings, most-severe first (empty array if none
survive). Use `category` values `architecture`, `naming`, `bug`, `complexity`, or
`performance`. Set `verdict: CONFIRMED` when certain, `PLAUSIBLE` when reasonable but not fully
verifiable from the diff alone (e.g. whether a request is truly duplicated at runtime). Do not
also print the findings as prose — `ReportFindings` is the output.

## Language

Match the user's language for every free-text field (narration before the tool call, finding
summaries) based on the language of their most recent message in this conversation — Vietnamese
in, Vietnamese out; otherwise English. Re-check this every run rather than assuming from a
previous session.
