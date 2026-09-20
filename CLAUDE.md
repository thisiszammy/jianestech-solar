# JianesTech Solar Fund — project guide

Guidance for working in this repository. `JianeTech.Services/CLAUDE.md` holds the binding
rules for the service layer; this file covers everything else.

## What the platform is

A solar investment fund with two sides:

- **Investors** place capital and receive a fixed profit interest, plus a solar
  installation at no cost.
- **Customers** take a solar installation and pay it off in fixed monthly instalments.
  Their current electricity bill is what matches them to a scheme — each one is
  recommended for bills up to a certain amount — and it pays for no part of it.

The Philippine context is baked into the schema: `Users.PhoneNumber` is `nvarchar(11)` for
local mobile numbers (`09XXXXXXXXX`).

Currently implemented: **authentication, account onboarding and the audit trail**, plus a
back-office console for administrators, and the two **scheme registers** — the instalment
terms a household can be offered and the investment terms capital can be placed on. The
agreements written against those schemes (`dbo.Installments`, `dbo.Investments`) and the
customer and investor registers have tables but no behaviour yet.

Accounts are opened two ways: an administrator invites from the console, or a visitor
registers themselves at `/register`. Both end at the same one-time link. **Because anyone
can now hold an account, the console is role-gated**: everything under `/manage` is
`UserTypeEnum.SystemAdmin` only, and a standard user lands on `/manage/account`, which is
where the investor and household views will grow. Before registration existed every
account had been created by an administrator, so `[Authorize]` alone was the whole of the
access control — if you are reading old code that assumes that, this is what changed.

A scheme's arithmetic lives in the **client**, not the service layer — `Schemes/SchemeMath.cs`
and `Schemes/InvestmentMath.cs`. Both configuration pages have to answer "what does this come
to?" on every keystroke, long before anything is saved, so a second copy on the server would
be two implementations of one calculation that could disagree. The server stores the terms and
enforces the one rule that makes each coherent; everything else is read back off them.
`dbo.InvestmentSchemes.IncomeInterest` is a **percentage per annum** (12.50 = 12.5% a year),
and the interest over a term is `placement x rate x years` — **simple, never compound**. The
whole return is worked out once and scheduled before the money is placed: no balance accrues,
and nothing paid out is reinvested. **No compound figure belongs anywhere on the investment
pages.** One was tried — the annual rate that would grow the placement into the same total —
and it read 10.79% beside a scheme quoting 12%, because compounding reaches a total from a
lower rate; it was also identical for both scheme structures, since it sees only the first
amount and the last. `InvestmentFigures.EffectiveAnnualRate` is the figure that separates
them, and it is annualised nominally (`monthly x 12`) for the same reason. Every payment is
**floored to the whole peso** and nothing settles the remainder — that is the product, not a
rounding convenience.

`dbo.InstallmentSchemes.ReferenceBillAmount` is **advisory**. It is the monthly electricity
bill a scheme is recommended up to — "for bills of ₱9,000 and under" — and the figure that
decides which households are offered it. It is not money the fund receives, and it pays no
part of the instalment: a household goes on paying whatever it is billed, and pays the
instalment as well. The register, the configuration page and the landing copy all once read
it as a bill the instalment *replaced*, and the editor drew the two running totals crossing
at "paid for itself · month 19" — a saving out of money that never changes hands. **Nothing
may net the bill against the instalment.** The one figure that relates them is
`SchemeFigures.InstalmentShareOfBill`, a proportion, which says how heavily a scheme presses
the households it is meant for; the chart that replaced the crossing one runs what the
household has paid against the **cash price** of the installation instead.

## Solution layout

Four projects, all `net10.0`, defined by `JianeTech.sln`:

- **`JianeTech.Api`** — ASP.NET Core host and composition root (`Program.cs`). Owns the
  JSON API plus cookies, JWT bearer, CORS, rate limiting and the silent-refresh
  middleware — and serves the Blazor console, so this is the only project you run.
- **`JianeTech.Data`** — EF Core 10 + SQL Server. Scaffolded entities, `DatabaseContext`,
  the dictionary-backed `UnitOfWork`, and one repository per entity.
- **`JianeTech.Services`** — business logic. See its own `CLAUDE.md`; those rules are binding.
- **`JianeTech.Client`** — Blazor WebAssembly console. Referenced by `JianeTech.Api` and
  served from it; it also runs standalone on its own dev server for hot reload.
  `JianeTech.Api/wwwroot/` is deliberately empty — the client's `wwwroot` is composed into
  it by static web assets, but the folder must exist for a web root to resolve.

## Common commands

```powershell
dotnet build
dotnet run --project JianeTech.Api      # https://localhost:7285 — starts everything
```

**One command runs the whole application.** `JianeTech.Api` references `JianeTech.Client`
and serves it, so the API host is also the console: `_framework/*` comes from the
referenced client, everything else from its `wwwroot`, and any unmatched non-API path
falls back to `index.html` for the Blazor router. The `https` profile opens a browser on
start.

There is no test project yet.

### Two topologies

| | Console served by | Origins | CORS |
| --- | --- | --- | --- |
| **Hosted** (default) | `JianeTech.Api` | same | none needed |
| Standalone | the client's own dev server | `:7071` → `:7285` | `Cors:AllowedOrigins` |

Hosted is the normal way to run, and the one the cookie design wants — nothing is
cross-origin, so nothing depends on the CORS allow-list agreeing with the cookie's
`SameSite` setting.

The standalone form still works for client hot-reload: run **both** projects, and open
`https://localhost:7071`. It needs the origin listed in `Cors:AllowedOrigins`; an empty
list means no CORS surface at all, which is the correct, tighter posture when nothing is
cross-origin.

```powershell
# hot-reload workflow only
dotnet run --project JianeTech.Api      # https://localhost:7285
dotnet run --project JianeTech.Client   # https://localhost:7071
```

## Database — database-first, no migrations

The schema lives in the existing `JianeTechSolar` database on `EXTRASERVER` and is **not**
managed from code. Entities are generated by **EF Core Power Tools** from
`JianeTech.Data/Entities/efpt.config.json`.

- Files under `JianeTech.Data/Entities/` marked `// <auto-generated>` are regenerable.
  Don't hand-edit them — re-run the scaffold and commit the output.
- Hand-written additions go in a **separate partial file** so the scaffold cannot clobber
  them: `ActivityLog.Factory.cs` extends the entity, and
  `DatabaseContext.AccessRefreshTokens.cs` extends the model through the generated
  `OnModelCreatingPartial` hook.
- `dbo.AccessRefreshTokens` was added after the initial scaffold by
  `JianeTech.Data/Scripts/001_AccessRefreshTokens.sql`. It is now listed in
  `efpt.config.json`, so the next scaffold generates it natively — at that point delete
  the two hand-written files above rather than merging them.
- Scaffold settings of note: `UseFluentApiOnly: true`, `UseDatabaseNames: true`,
  `UseNullableReferences: false` (matching the `#nullable disable` on generated files).
- **Never call `Database.Migrate()`.** There are no migrations, and doing so would try to
  take ownership of a schema this project does not own.

Connection string comes from `JianeTech.Api/.env` (`DotNetEnv` with `TraversePath()`),
keyed `ConnectionStrings__DefaultConnection`.

## Authentication — JWT in HttpOnly cookies

The client never sees a token. Both cookies are `HttpOnly`, so an XSS bug cannot exfiltrate
a session.

| Cookie   | Holds                                    |
| -------- | ---------------------------------------- |
| `jt_atk` | The signed JWT access token.             |
| `jt_rtk` | The raw refresh-grant Guid.              |

`dbo.AccessRefreshTokens` stores only the **SHA-256** of the refresh Guid, so a leaked row
cannot be replayed as a cookie value.

### The three halves that must agree

In the hosted topology the cookies are first-party and none of this is load-bearing. It
becomes load-bearing the moment the console is served from somewhere else, and then
**all three** must hold. Change one and calls still succeed — they just arrive
unauthenticated, which looks like a broken session rather than a missing header:

1. `AuthenticationCookies` sets `SameSite=None; Secure` (requires HTTPS both ends).
2. The API's CORS policy calls `.AllowCredentials()` with explicit origins
   (`Cors:AllowedOrigins` — `AllowAnyOrigin` is incompatible with credentials).
3. The client's `CredentialsHandler` sets `BrowserRequestCredentials.Include`.

`SameSite=None` is kept rather than relaxed to `Lax` because it is correct in **both**
topologies. The cost is that `Secure` — and therefore HTTPS — is mandatory: over plain
HTTP the browser drops the cookie silently and sign-in appears to do nothing.
`UseHttpsRedirection` covers the normal case.

### Session lifecycle

- **Sign in** verifies the password (PBKDF2-SHA256 with a per-user salt — Identity is not
  in use), supersedes any grant still open for the account, mints both cookies, and writes
  a `UserLoggedIn` audit row.
- **Silent refresh**: `AccessTokenRefreshMiddleware` runs before `UseAuthentication`. When a
  request arrives with an expired `jt_atk` but a valid `jt_rtk`, it mints a fresh token,
  hands it to the bearer handler for that same request via `HttpContext.Items`, and
  re-stamps both cookies. The client makes one request and gets one answer.
- **Rotation**: a short-lived grant is consumed on use and replaced. A long-lived
  ("remember me") grant is reused until expiry and writes no audit row, because it performs
  no database write.
- **Replay detection**: presenting a grant whose row is already `UsedOn` revokes **every**
  live grant for that account and writes `RefreshTokenReplayDetected`.
- **Lock-out**: five failed passwords sets `IsLocked`. The failed attempt is persisted in
  its own transaction so it survives the exception that follows.

### Onboarding and recovery — nobody types a password for anybody else

An account is never created with a password. The console **invites** and the public site
**registers**, and either way the person it belongs to sets their own credential from a
one-time link. That one decision is what the rest of this section follows from.

| Flow | Route | Table | Life |
| --- | --- | --- | --- |
| Invitation | `/activate?t={guid}` | `dbo.UserActivationTokens` | 7 days, single use |
| Registration | `/register` → the same `/activate` link | `dbo.UserActivationTokens` | 7 days, single use |
| Recovery | `/reset-password?t={guid}` | `dbo.PasswordResetTokens` | 1 hour, single use |

- **Registration and invitation produce the same kind of row**, and meet at the same
  activation link. `UserService.SelfRegisterUserAsync` is `InviteUserAsync`'s twin, and
  the two differences are both security rather than options: the role is fixed to
  `UserTypeEnum.User` and never read from the request, and there is no `createdBy`,
  because nobody at the fund created the account. The audit row is
  `ActivityEnum.UserSelfRegistered` with the registrant themselves as the actor.
- **`POST /api/account/register` will not confirm that an email address is on file.**
  Success and "that address already has an account" answer 200 with the same sentence,
  byte for byte — any difference makes the endpoint a way of testing which addresses
  belong to the fund's investors and customers. That is what `DuplicateAccountException`
  exists for: the validator raises it, and the endpoint catches it *before* its base type
  and answers as if nothing had happened. A taken **username** is reported plainly, because
  the person cannot finish the form otherwise. On `auth-strict` (5/min per IP): it is an
  anonymous write that also sends mail.

- **The link carries a raw Guid; the table stores only its SHA-256** (`TokenHasher`), exactly
  as `dbo.AccessRefreshTokens` does for refresh grants. The raw value exists for the length
  of one service call and goes to one place — `IEmailService`. It is never returned from a
  service, never logged, and never reaches a DTO. That is also why testing these flows means
  planting a known hash in the table rather than reading a token back out.
- **An invited account is `IsActive = true` with an unusable random credential**
  (`PasswordHasher.CreateUnusable`). `IsActive` records an administrator's decision about
  whether an account may be used at all, and being newly invited is not that decision.
  "Waiting on an invitation" is derived from the token rows instead and surfaces as the
  **Pending** status — see `UserService.ResolveStatus` for the order it sits in.
- **Issuing a link and sending the mail are separate acts, and the code says which
  happened.** The row is committed first, because the link has to exist before it can be
  clicked, and Brevo is called outside the transaction — holding one open across an HTTP
  call to a third party is how a slow mail server becomes a lock-wait timeout. The audit
  wording follows: rows read as *issuing* a link, so they stay true when delivery fails.
- **A delivery failure is reported, not hidden, and not rolled back.** `InviteUserAsync`
  returns `InvitationResultDTO` carrying the account *and* whether the mail left; the
  endpoint answers 200 either way and the register paints the partial outcome amber rather
  than green. Resend and admin-triggered reset, whose whole purpose is the mail, let
  `EmailDeliveryException` reach the caller as a 400.
- **The anonymous request endpoint is deliberately uninformative.** `POST
  /api/account/password-reset` answers with one sentence whatever happened — unknown
  identifier, inactive account, unspent invitation, bounced mail — because any variation
  turns it into a way of enumerating the register. Real outcomes go to the log. The
  administrator-operated path reports plainly, since that caller can already see the row.
- **Resetting a password revokes every live refresh grant for the account.** "I forgot my
  password" and "someone else is in my account" arrive through the same door.

Templates live in `docs/email/` and are imported into Brevo as **raw HTML**, not through the
drag-and-drop builder — each file's header comment says why, and lists the `params` it takes.
The parameter names are a contract with the template; activation takes `ACTIVATION_URL` and
recovery takes `RESET_URL`, deliberately different so a body pasted into the wrong template
renders a dead button rather than quietly sending people into the other flow.

### Two ordering traps

Both are load-bearing and commented at the call site:

- `AccessTokenRefreshMiddleware` must run **before** `UseAuthentication`, or the bearer
  handler looks for a token before one has been minted.
- The JWT `OnChallenge` handler must skip its defensive cookie-clear when the middleware
  refreshed on that request. `IResponseCookies.Delete` strips matching earlier `Append`s
  from the same response, so clearing there would wipe the just-rotated cookies and turn a
  recoverable challenge into a permanent sign-out.

## API endpoint rules

**Strict**, and they apply to every endpoint under
`JianeTech.Api/Controllers/ApiControllers/`. `AuthenticationApiController` is the template.

1. **Exception handling — explicit, per service method.** Every endpoint body is wrapped in
   a `try` followed by:
   - One `catch` per **custom** exception the called service method can throw (the types in
     `JianeTech.Services/Exceptions/`), returning `400` with `Message = ex.Message`. Catch
     derived types (`AccountLockedException`) **before** their base.
   - A final `catch (Exception ex)` returning `500` with
     `Message = "Unknown server error occurred"` — verbatim, do not vary the wording.

   No exception may escape an endpoint. Read-only services throw no service-scoped
   exception, so their endpoints carry only the generic catch.

2. **Consistent response body — `ApiResponse`.** Every endpoint returns
   `JianeTech.Api.Models.Responses.ApiResponse` with `Code` (mirrors the HTTP status),
   `Message`, and `Data` (the payload, or `null` on error). The HTTP status the action
   returns must match `ApiResponse.Code`.

3. **Structured logging on every endpoint.**
   - `var logId = Guid.NewGuid();` as the first line.
   - Entry: `_logger.LogInformation("{LogId} : {Process} -> {@Parameters}", …)` where
     `Process` is `nameof(TheAction)` and `Parameters` is an anonymous object of the inputs.
   - Success: the same template with a `"succeeded"` suffix on the process string.
   - Custom-exception catch: `_logger.LogWarning(ex, …)` — **exception always first**.
   - Base catch: `_logger.LogError(ex, …)`.
   - **Never log secrets.** Build the parameters object explicitly and omit passwords,
     tokens and salts — see `Authenticate`, which logs the username but not the password,
     and `Refresh`, which logs only *whether* a cookie arrived.

4. **Acting user id.** Resolve via `HttpContext.GetActingUserId()` and pass the resulting
   `Guid?` straight through. **Do not coerce missing values to `Guid.Empty`** — audit
   columns must persist as SQL `NULL` when the actor is unknown. Never accept an acting
   user id from a request body.

5. **Auth attributes.** `[Authorize(Roles = ConsoleRoles.Administrator)]` for anything that
   runs the fund — users, dashboard, activity logs, both scheme registers. Plain
   `[Authorize]` only where every account holder belongs (`/api/auth/me`, logout).
   `[AllowAnonymous]` on public flows (sign in, refresh, and all of
   `AccountApiController`). Rate-limit credential submission with
   `[EnableRateLimiting("auth-strict")]` (5/min per IP) and session upkeep with
   `"auth-general"` (60/min per IP).

   **The role string is the `UserTypeEnum` member's name, not its label** — "SystemAdmin",
   never "Administrator". `JwtTokenGenerator` writes it, `Program.cs` pins
   `RoleClaimType` to the same constant, and `ConsoleRoles` holds the one copy on each
   side. A policy written against the display label compiles, runs, and refuses every
   administrator in the fund.

## Client conventions

- **The server is the only authority on whether a session exists.** The client cannot read
  its own token, so `CookieAuthenticationStateProvider` asks `GET /api/auth/me` and caches
  the *Task* — not a bool — so concurrent callers on a cold load share one request.
- Pages carry `[Authorize]`; `AuthorizeRouteView` redirects to `/login` via
  `RedirectToLogin` rather than rendering an empty console.
- `FundApiClient` returns the API's envelope rather than throwing, so a page can render the
  server's wording verbatim. A `401` is the one status handled structurally.
- Styles live in `wwwroot/css/` (tokens → base → components → pages), linked from
  `index.html`. There are no `.razor.css` scoped files — don't add the
  `JianeTech.Client.styles.css` link back unless you introduce one, or it 404s.
- `ApiBaseUrl` is **unset** in `wwwroot/appsettings.json`, so the console calls whichever
  origin served it — correct when the API hosts it, and correct in production.
  `appsettings.Development.json` pins it to `https://localhost:7285/`, which is right in
  both topologies: the same origin when hosted, the API's origin when standalone.

### The landing page — public, and mostly placeholder

`Pages/Landing.razor` is the root route, and the only page written for someone who has
never heard of the fund. It is public simply by carrying no `[Authorize]`:
`AuthorizeRouteView` only waits on `GET /api/auth/me` for pages that ask for authorization,
so this one paints at once. The console lives under `/manage`.

- **The one call goes to `/register`, worded "Open an account".** The page asks for the auth
  state without waiting on it, so a visitor who is already signed in is offered "Open the
  console" → `/manage` instead. Because the call no longer points at `/login`, **signing in
  needs a door of its own** and has one in four places: the bar (from 900px), the phone
  sheet, the closing panel and the footer. `Login.razor` carries the way back across. A
  returning visitor must never have to read a page about joining to find the way in.
- **Dashed means "not filled in", and nothing else on the page may be dashed.** Every picture
  is an `ImageSlot` — an empty panel array carrying a nameplate that states the photograph
  wanted and the size to supply it at — and every figure, name or contact detail nobody has
  supplied yet wears `.ph`. Rates and terms are zeros in a dashed box, never plausible
  inventions: a fund's landing page must not show a number that could be read as an offer.
- **All of the copy is data**, in the `@code` block at the foot of `Landing.razor`. Replacing
  it is an edit to those lists. Dropping a record's `Placeholder` flag removes its mark, and
  giving an `ImageSlot` a `Src` swaps the placeholder for the image in the same box — the
  ratio is pinned, so a file of the wrong shape is cropped rather than reflowing the page.
- **Its links are bare fragments (`#questions`), and they depend on the page living at the
  base path.** `index.html` sets `<base href="/">`, so they resolve to `/#questions`, the same
  document, and Blazor scrolls. Move the page and each one needs its route in front of the
  hash, or it navigates to the root instead. (Blazor scrolls to a fragment but does not move
  focus, which is why the skip link is handled in code rather than followed.)
- **`FundCircuit` is the page's one orchestrated moment**: the fund drawn as a routed board
  with the seal at the junction — gold run out, cyan run back, lit once, nothing looping. It
  has two routings because its labels are SVG text and scale with the drawing; the wide one
  falls under 12px below a 960px viewport, where the tall one takes over.
- Bright gold stays on the dark panel here as everywhere: `.btn--brand` and the gold run live
  only on `.landing__panel`, the class that also flips `--focus` to `--gold-lit` in `tokens.css`.

## Design system

`wwwroot/css/tokens.css` is the single source of truth — mirror changes there, not at call
sites.

- **Palette**: photovoltaic silicon, not foliage. The structural colour is panel indigo
  (`--ink`), with one warm late-light accent (`--solar`) on cool ledger paper (`--paper`).
  **Green appears only as the `--success` status semantic, never as brand.**
- `--panel-dark` is the fixed dark field (sidebar, login panel). It does **not** invert in
  dark mode — the panel is a physical object, and everything drawn on it assumes a dark
  ground. `--ink` *does* invert, so never use it as a background for white text.
- **Type**: one family, Archivo. Numbers use `font-variant-numeric: tabular-nums` so
  columns of figures align.
- **Layout**: figures sit in a ruled rail (label left, value right, dotted leader between),
  not in KPI cards. The activity ledger is the spine of the page.
- Motion answers a person's action. There are two non-interactive moments and each runs
  once: the login array fading up, and the landing page's circuit lighting.
  `prefers-reduced-motion` is honoured globally in `base.css` — which collapses durations
  but not delays, so anything staggered must also zero its own `animation-delay` there.
- Status is never carried by colour alone — every dot sits beside its label.

## Conventions

- Target framework `net10.0`, `Nullable` enabled (disabled at file level on scaffolded
  entities — leave that alone).
- **Local time only.** Use `IDateTimeService.Now()`; `DateTime.UtcNow` is banned.
- Soft-delete only. Nothing is removed by `DELETE`.
- **`.env` lives in `JianeTech.Api/`, beside `Program.cs` — not at the solution root.**
  It is git-ignored; `JianeTech.Api/.env.example` is the committed template listing every
  key. `TraversePath()` is kept rather than a plain `Load()` because the working directory
  is the project folder under `dotnet run` but the output folder under a built exe, and
  only traversal reaches the file from both. `Jwt__Key` must be at least 32 bytes — the
  app refuses to start otherwise.
- **Brevo settings are validated at send time, not at startup.** `Brevo__ApiKey`,
  `Brevo__ActivationTemplateId`, `Brevo__PasswordResetTemplateId` and `App__ConsoleBaseUrl`
  are read once but checked when a mail is actually sent, so a console with no Brevo account
  yet still starts, still signs people in and still creates accounts — only the mail fails,
  and it fails naming the key that is missing.
- First-run seeding creates one administrator when `Users` is empty, driven by `Seed__*`.
  It goes through `IUserService` so the first account earns a real `UserRegistered` audit
  row. Leave `Seed__AdminPassword` blank to skip it.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **jianes-tech-solar** (93 symbols, 99 relationships, 0 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/jianes-tech-solar/context` | Codebase overview, check index freshness |
| `gitnexus://repo/jianes-tech-solar/clusters` | All functional areas |
| `gitnexus://repo/jianes-tech-solar/processes` | All execution flows |
| `gitnexus://repo/jianes-tech-solar/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
