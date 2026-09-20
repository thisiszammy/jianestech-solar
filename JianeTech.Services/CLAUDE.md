# JianeTech.Services

Implementation guide for the service layer of the JianesTech Solar Fund platform.
These rules are binding: an API controller's exception handling, the audit trail's
completeness, and the transaction boundaries all depend on services being written
exactly this way.

## A. Project Structure

```
JianeTech.Services/
├── Services/                       ← service implementations (one per resource)
│   ├── AuthenticationService.cs    ← session lifecycle: sign in, rotate, sign out
│   ├── UserService.cs              ← canonical reference for the write pattern
│   ├── DashboardService.cs         ← read-only aggregate
│   ├── ActivityLogService.cs       ← read-only paged audit feed
│   └── DateTimeService.cs          ← infrastructure helper (the clock)
├── Interfaces/                     ← service contracts (one per service)
│   ├── I{Resource}Service.cs
│   ├── IClientContextAccessor.cs   ← IP / User-Agent for the audit trail
│   ├── IDateTimeService.cs
│   └── IJwtTokenGenerator.cs
├── DTOs/                           ← output types crossing the service boundary
│   ├── {Entity}DTO.cs
│   └── QueryableEntityDto.cs       ← pagination envelope (Items + TotalRows)
├── Validators/                     ← stateless validator classes (registered Singleton)
│   └── {Resource}Validator.cs
├── Exceptions/                     ← per-service custom exceptions
│   └── {Resource}ValidationException.cs
├── Security/                       ← credential and token primitives
│   ├── PasswordHasher.cs           ← PBKDF2-SHA256, per-user salt
│   ├── TokenHasher.cs              ← SHA-256 of a refresh-token Guid
│   └── PasswordRules.cs            ← the password policy, in one place
├── JwtTokenGenerator.cs            ← access-token minting (Singleton)
└── HttpClientContextAccessor.cs    ← IClientContextAccessor over IHttpContextAccessor
```

The folder layout is fixed. Add new resources into the existing folders rather than
introducing per-feature folders.

## B. Naming & Layout

- One service per resource. File: `Services/{Resource}Service.cs`. Interface:
  `Interfaces/I{Resource}Service.cs`.
- State-mutating method names: `{Verb}{Resource}Async` (e.g. `RegisterUserAsync`,
  `AuthenticateUserAsync`, `LogoutAsync`). Read-side names: `Get{Resource}DTOs`,
  `Get{Resource}DTOById` (e.g. `GetDashboardSnapshotDTO`, `GetActivityLogDTOs`,
  `GetAuthenticatedUserDTOById`).
- Register every service as **Scoped** in `JianeTech.Api/Program.cs`:

  ```csharp
  builder.Services.AddScoped<IUserService, UserService>();
  ```

- Register the matching validator as **Singleton** — by concrete type, not by interface:

  ```csharp
  builder.Services.AddSingleton<UserValidator>();
  ```

- `IDateTimeService` and `IJwtTokenGenerator` are **Singleton**: both are stateless and
  read their configuration once.

### Rule 1 — Primitive Parameters Only

Service methods accept only primitive-shaped values. **Never** accept ViewModels, DTOs,
request bodies, or entities — the API controller unpacks those into discrete primitives
before calling the service.

Allowed parameter types:

- Primitives: `string`, `int`, `long`, `double`, `decimal`, `bool`
- Primitive-wrapper structs: `Guid`, `DateTime`, `TimeSpan`
- Any `enum` defined in `JianeTech.Data.Enums`
- `List<T>` / `IEnumerable<T>` of any of the above
- `CancellationToken` — always the **last** parameter, forwarded from the API controller

Allowed return shapes: `Task<{primitive}>` for state-mutating methods that produce an id
(e.g. `Task<Guid>`), `Task` for void mutations, `Task<{Entity}DTO>` /
`Task<List<{Entity}DTO>>` / `Task<QueryableEntityDto<{Entity}DTO>>` for reads. Never
return an entity from a service.

## C. Audit/Activity Log on every Database write

Every write to the database is paired with an `ActivityLog` row.

1. **Generate one `batchId` per service method call** (`Guid.NewGuid()`). All
   `ActivityLog` rows produced by that call share this `batchId` so the entire operation
   is traceable as a single audit batch.
2. **Maintain an `executionOrder` counter** that pre-increments per log line. Start at
   `0`; the first row is `1`, the second `2`. Multiple writes in the same method share the
   `batchId` and continue incrementing in order.
3. **Build via the factory**: `ActivityLog.Create(batchId, executionOrder, activity,
   executedBy, executedOn, ipAddress, userAgent, detail, referenceType, reference)`.
   Never construct `ActivityLog` by property assignment. The factory also clamps
   `UserAgent` / `IpAddress` / `Detail` to their column widths — a 400-character
   User-Agent must never turn a rejected sign-in into a 500.
4. **Build the detail string via `ActivityLog.BuildDetail(activity, …args)`** so the
   wording stays consistent for that activity across the codebase.
5. **Always include `ReferenceType` and `Reference`.** Pass the
   `ActivityLogReferenceTypeEnum` value for the entity being acted on plus that entity's
   primary key. These columns are nullable for non-domain (system-wide) actions; domain
   actions always set them.
6. **Add a new `ActivityEnum` value** in `JianeTech.Data/Enums/ActivityEnum.cs` whenever a
   new write operation has no existing entry, and give it wording in
   `ActivityEnumExtensions` (`ToDetail` / `ToLabel` / `ToStatus`). Add a new
   `ActivityLogReferenceTypeEnum` value when introducing a new reference target. Enum
   values are a persisted contract — append, never renumber or repurpose.
7. **Source `IpAddress` and `UserAgent` from `IClientContextAccessor`** (registered
   transient against `HttpClientContextAccessor`). Never accept them as service parameters
   or from a request body. Outside an HTTP request (startup seeding) both resolve to null
   and the factory records `"Unknown"` — that is correct, not a gap to paper over.
8. **`executedBy` is `Guid?` and stays null when the actor is unknown.** Never coerce to
   `Guid.Empty`: the all-zero Guid is unfilterable and silently breaks audit queries.

## D. Database Writes Wrapped in Try / Catch with Rollback

DB writes are committed via `_unitOfWork.CommitAsync<DatabaseContext>(cancellationToken)`
inside a `try`. The **first** statement inside the `try` is **always**
`await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken)` —
`CommitAsync` throws `InvalidOperationException` ("No active transaction for
DatabaseContext.") if no transaction is open. On any exception, roll back inside a nested
`try/catch` (so a rollback failure cannot mask the original exception) and rethrow. The
exception then propagates to the API controller, which catches the service-scoped
`{Resource}ValidationException` for a `400` or falls through to its generic `500`.

Validation runs **before** the `try` region — a validation failure must not enter the
transactional region.

```csharp
try
{
    await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

    _userRepository.AddUser(newUser);
    _activityLogRepository.AddActivityLog(registeredLog);

    await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
}
catch
{
    try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
    catch { }
    throw;
}
```

Specify the `<DatabaseContext>` generic on every `BeginTransactionAsync` / `CommitAsync` /
`RollbackAsync` call — the unit of work is dictionary-backed and supports multiple
`DbContext` types.

**A method may open more than one transaction.** `AuthenticationService` writes the failed
attempt in its own transaction and then throws, because that row must persist even though
the call is failing. `BeginTransactionAsync` is re-entrant per context, so this is safe.

## E. External API Calls Log via `ILogger<TService>` with `[ServiceCaller]` Prefix

Pure DB-mutation services do **not** inject `ILogger`. None of the current services call an
external API, so none of them inject one. A service that wraps a call to another service or
a third-party HTTP API injects `ILogger<TService>` and emits start / success / failure lines
mirroring the API endpoint template, **prefixed with `[ServiceCaller]`** so the log reads as
"this service was the caller, not the endpoint":

```csharp
var logId = Guid.NewGuid();
var process = nameof(CallExternal);

try
{
    _logger.LogInformation("[ServiceCaller] {logid}: Method {process} Started. -> {@data}", logId, process, request);

    var response = await _externalClient.CallAsync(request, cancellationToken);

    _logger.LogInformation("[ServiceCaller] {logid}: Method {process} Finished Successfully. -> {@response}", logId, process, response);

    return response;
}
catch ({ExpectedException} ex)
{
    _logger.LogWarning(ex, "[ServiceCaller] {logid}: Method {process} failed with expected error", logId, process);
    throw;
}
catch (Exception ex)
{
    _logger.LogError(ex, "[ServiceCaller] {logid}: Method {process} failed with unexpected error", logId, process);
    throw;
}
```

The service's `logId` is independent of the API controller's `logId`; the pair is correlated
at log-query time by timestamps and request path. Use Serilog destructuring (`{@data}`,
`{@response}`) on bound objects so structured fields are preserved.

## F. Validator + Custom Exception per Service

Each service that **writes** ships with:

- A **validator** at `Validators/{Resource}Validator.cs` (e.g. `UserValidator`).
- A **custom exception** at `Exceptions/{Resource}ValidationException.cs` (e.g.
  `UserValidationException`) deriving from `Exception` with a single `string message`
  constructor.

**Read-only services ship neither.** `DashboardService` and `ActivityLogService` perform no
writes and throw no service-scoped exception, so their endpoints carry only the generic
`catch (Exception)`. Do not invent an unused validator to satisfy symmetry.

Validator rules:

- **Stateless** — no instance fields. All inputs flow through method parameters. Required
  because the validator is registered as `Singleton` and is shared across all callers and
  threads.
- One public method per validation scenario (`ValidateRegisterUser`,
  `ValidateSignInCandidate`, `ValidateRefreshToken`, …). Returns `Task` if asynchronous,
  `void` otherwise.
- Throws **only** the service-scoped exception. A validator may throw a type *derived* from
  it when the caller needs to distinguish the case — `AuthenticationValidator` throws
  `AccountLockedException` and `RefreshTokenReplayException`, both of which derive from
  `AuthenticationValidationException`. A controller that catches only the base type still
  maps them to `400`; one that catches the derived type first can render a dedicated state.
  Never throw bare `Exception` or another service's exception type.
- All cross-row / "already exists" / "not found" / uniqueness checks live in the validator.
  Trivial null checks on a freshly created entity can stay inline in the service; anything
  that touches another row goes through the validator.
- **Keep string-length rules in step with the column widths** in
  `JianeTech.Data/Entities/DatabaseContext.cs`. An over-long value should be a `400` with a
  readable message, not a `500` from SQL Server refusing the insert.

## Canonical Service Method (use as a reference)

`UserService.RegisterUserAsync` is the reference implementation — read it alongside this.

```csharp
public async Task<Guid> RegisterUserAsync(
    string firstName,                                 // Rule 1: primitives only
    string lastName,
    string username,
    string email,
    string phoneNumber,
    string password,
    UserTypeEnum userType,
    Guid? createdBy,                                  //         nullable: never Guid.Empty
    CancellationToken cancellationToken)              //         always last
{
    // §C: the audit context shared by every ActivityLog row this method emits.
    var batchId = Guid.NewGuid();
    var executionOrder = 0;
    var timestamp = _dateTime.Now();                  // global rule: local time only

    var (hash, salt) = PasswordHasher.Hash(password ?? string.Empty);

    User newUser = new()
    {
        UserId = Guid.NewGuid(),
        Username = username?.Trim() ?? string.Empty,
        Password = hash,
        Salt = salt,
        UserType = (int)userType,
        IsActive = true,
        CreatedOn = timestamp,
        CreatedById = createdBy,
        // …
    };

    // §C: the ActivityLog paired with the upcoming insert. BatchId groups it with
    // any further rows this call emits; pre-increment puts the first at 1.
    // IpAddress / UserAgent come from IClientContextAccessor — never parameters.
    // Detail goes through BuildDetail so the wording is consistent for this
    // activity. ReferenceType + Reference let an audit query target the row.
    var registeredLog = ActivityLog.Create(
        batchId,
        ++executionOrder,
        ActivityEnum.UserRegistered,
        createdBy,
        timestamp,
        _clientContextAccessor.IpAddress,
        _clientContextAccessor.UserAgent,
        ActivityLog.BuildDetail(ActivityEnum.UserRegistered, newUser.Username),
        ActivityLogReferenceTypeEnum.User,
        newUser.UserId);

    // §F: validate BEFORE the transactional region. A UserValidationException
    // becomes a 400 at the controller with no DB work attempted.
    await _validator.ValidateRegisterUser(
        newUser, password ?? string.Empty, _userRepository.GetUsers(), cancellationToken);

    // §D: commit inside try / catch. The first statement opens the transaction.
    // Nested try around the rollback so a rollback failure cannot mask the original.
    try
    {
        await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

        _userRepository.AddUser(newUser);
        _activityLogRepository.AddActivityLog(registeredLog);

        await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
    }
    catch
    {
        try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
        catch { }
        throw;
    }

    return newUser.UserId;
}
```

Methods with multiple writes reuse the same `batchId` and continue incrementing
`executionOrder` across every `_activityLogRepository.AddActivityLog(...)` call.

## G. DTO Conventions

- DTOs live in `DTOs/`. Naming: **`{Entity}DTO.cs`** (uppercase `DTO`, e.g.
  `ActivityLogDTO`, `AuthenticatedUserDTO`, `DashboardSnapshotDTO`).
- DTOs are flat property-bag classes. Never expose `JianeTech.Data.Entities.*` outside the
  Services project — services materialize DTOs at the query boundary.
- **Never put credential material in a DTO.** `Password` and `Salt` do not cross the
  service boundary in any shape.
- **Always use method syntax (fluent/lambda) for EF Core queries.** Never use LINQ query
  syntax (`from x in ... where ... select ...`). Use `.Where(x => ...)`, `.Select(x => ...)`,
  `.OrderBy(x => ...)`.
- **Project to DTO inside the EF query** so the SQL only selects the columns the DTO needs:

  ```csharp
  return await _userRepository.GetUsers()
      .Where(u => u.UserId == userId && u.DeletedOn == null)
      .Select(u => new AuthenticatedUserDTO
      {
          UserId    = u.UserId,
          FirstName = u.FirstName,
          LastName  = u.LastName,
          Username  = u.Username,
      })
      .FirstOrDefaultAsync(cancellationToken);
  ```

- **C# switches and extension methods cannot be translated to SQL.** When a projection needs
  one (e.g. `ActivityEnum.ToLabel()`), project to an anonymous type in the query, then map to
  the DTO in memory. `ActivityLogService.GetActivityLogDTOs` is the reference for this.
- For paginated reads, return `QueryableEntityDto<{Entity}DTO>` with `Items` (page slice) and
  `TotalRows` (filtered total used for page-count math). Compute `TotalRows` from the same
  filtered query **before** `Skip` / `Take`.
- **Clamp paging inputs rather than rejecting them.** Page and page-size arrive from a query
  string; a nonsense value should degrade to a sane default, not 500 the feed.

## H. Creating new services

1. **Interface** — `Interfaces/I{Resource}Service.cs` declaring the public method surface
   (primitives in + `CancellationToken` last; primitives or DTOs out).
2. **Implementation** — `Services/{Resource}Service.cs`. Inject the resource repository, any
   cross-resource repositories, `IUnitOfWork`, `IDateTimeService`, `IActivityLogRepository`,
   `IClientContextAccessor`, and the validator. Inject `ILogger<{Resource}Service>` **only**
   if the service calls external APIs (§E).
3. **Validator** — `Validators/{Resource}Validator.cs`. Stateless. Throws the service-scoped
   exception. Skip for read-only services.
4. **Exception** — `Exceptions/{Resource}ValidationException.cs`. Skip for read-only services.
5. **DTOs** — `DTOs/{Resource}DTO.cs`. Project inside the EF query.
6. **Repository** — if the resource has no repository yet, add
   `Interfaces/Database/I{Entity}Repository.cs` and `Repositories/Database/{Entity}Repository.cs`
   in `JianeTech.Data`, deriving from `BaseRepository<DatabaseContext>`.
7. **Enum entries** — add `ActivityEnum` values for new write operations (with wording in
   `ActivityEnumExtensions`) and `ActivityLogReferenceTypeEnum` values for new reference
   targets.
8. **Register in `JianeTech.Api/Program.cs`**:

   ```csharp
   builder.Services.AddScoped<I{Resource}Service, {Resource}Service>();
   builder.Services.AddSingleton<{Resource}Validator>();
   ```

## Don'ts

- **Don't accept ViewModels, DTOs, or entities as service parameters.** Unpack to primitives
  at the API controller (Rule 1).
- **Don't accept user identifiers (UserId / IP / UserAgent) from a request body.** Source
  from `IClientContextAccessor` (IP / UA) or from the `createdBy` / `updatedBy` / `deletedBy`
  parameter the API controller forwarded.
- **Don't return entities from service methods.** Return primitives, DTOs, or
  `QueryableEntityDto<{Entity}DTO>`.
- **Don't let credential material into a DTO or a log.** No `Password`, no `Salt`, no raw
  token id.
- **Don't write to the DB without a paired `ActivityLog` row** sharing the call's `batchId`
  (§C).
- **Don't construct `ActivityLog` via property assignment.** Use `ActivityLog.Create(...)`
  and `ActivityLog.BuildDetail(...)`.
- **Don't put DB writes outside the try / catch with rollback** (§D). A partial commit on
  failure is worse than a clean rollback.
- **Don't call `CommitAsync` without first calling `BeginTransactionAsync`** in the same try
  block.
- **Don't validate inside the transactional region.** A thrown
  `{Resource}ValidationException` should never start a transaction.
- **Don't reuse an existing `ActivityEnum` value for a different action**, and never renumber
  one. Append.
- **Don't store state in validators.** Singleton lifetime → instance fields are shared across
  all callers and all threads.
- **Don't throw `Exception` (or a foreign service's exception type) from a validator.**
- **Don't coerce a missing actor to `Guid.Empty`.** `executedBy` / `createdBy` stay `null`.
- **Don't use `DateTime.UtcNow`.** Use `_dateTime.Now()` (or `DateTime.Now` where
  `IDateTimeService` is not injected) per the global DateTime rule.
- **Don't hard-delete.** Soft-delete via `DeletedOn` / `DeletedBy`; reactivate by nulling them
  and stamping `UpdatedOn` / `UpdatedBy`.
- **Don't omit the `<DatabaseContext>` generic** on `CommitAsync` / `RollbackAsync`.
- **Don't share `logId` between the API controller and the service.** Each layer mints its own.
- **Don't emit `[ServiceCaller]`-prefixed logs from a service that doesn't call an external
  API.**
- **Don't use LINQ query syntax for EF Core queries.** Method syntax only.
- **Don't hand-edit the scaffolded entities** in `JianeTech.Data/Entities/`. They are
  regenerated by EF Core Power Tools from `efpt.config.json`; put hand-written additions in
  a separate partial (see `ActivityLog.Factory.cs`).
