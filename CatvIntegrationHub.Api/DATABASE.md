# CasHub persistence

The existing ASP.NET Core 6 API is the presentation/composition root. The solution is `D:\Somavission\Working\CatvIntegrationHub\CatvIntegrationHub.sln`, with four separate sibling project folders:

```text
CatvIntegrationHub/
  CatvIntegrationHub.sln
  CatvIntegrationHub.Api/
  CatvIntegrationHub.Domain/
  CatvIntegrationHub.Application/
  CatvIntegrationHub.Infrastructure/
```

- Domain: `SvCard` and `SvCardStatus`; no EF dependency.
- Application: database-independent read and operation-recording contracts and DTOs; references Domain.
- Infrastructure: EF Core 6 SQL Server context, exact existing-table mappings, query and operation-insert implementations and dependency registration; references Application.
- API: controllers depend on `ISvCardReader` and `ISvCardOperationRecorder`; Program wires Infrastructure into scoped dependency injection.

CAS protocol packet behavior is unchanged. Successful POST commands now append an operation record to SvCards using the database status enum. Database status IDs are distinct from CAS terminal status bytes. No status rows are seeded automatically.

## Configuration

Use `ConnectionStrings:CasHub` in development user secrets (UserSecretsId: `CatvIntegrationHub.Api-CasHub`). In IIS/production, configure the environment variable `ConnectionStrings__CasHub` with the supplied connection string; Development user secrets are not loaded in Production. Restart the application pool after changing its configuration. Use one backslash between the server address and named instance in an environment variable; JSON requires `\\` to represent that backslash.

The checked-in connection string is empty by design. Do not put passwords into source control. The supplied named instance uses SQL Server instance discovery; if discovery is unavailable, obtain its actual TCP port and use `Server=103.18.205.218,PORT` rather than guessing a port. Prefer a dedicated database login with only the required permissions when provisioning production access.

## Existing database

Both tables must already exist in `CasHub`. Startup never calls `Migrate`, `EnsureCreated`, or `EnsureDeleted`. No migration scripts or changes to existing data are applied.

`SvCards.Id` is a bigint identity; `SvCardStatuses.Id` is a manually assigned smallint. String columns are non-Unicode varchar(50). `CreatedDate` uses the existing SQL `getdate()` default (SQL server local time); it is not presented as UTC. Nullable columns and the non-cascading foreign key match the supplied DDL. CardId has no unique constraint in the supplied schema, so detail requests use the numeric primary key.

## Read endpoints

- `GET /api/SvCards?page=1&pageSize=50` (pageSize 1–100): paginated cards with status names.
- `GET /api/SvCards/{id}`: one card by database primary key, or 404.
- `GET /api/SvCardStatuses`: existing lookup values, ordered by ID.

Queries are asynchronous, cancellation-aware, and do not track entities. These endpoints read database records only. They do not send CAS commands. Successful CAS POST commands use the operation-recording workflow below.

## Build

Run `dotnet restore CatvIntegrationHub.Api.csproj` then `dotnet build CatvIntegrationHub.Api.csproj --no-restore`. The SQL Server provider is pinned to 6.0.36 to match EF Core 6 / .NET 6. The existing .NET 6 end-of-support build warning remains.

## Recording successful CAS operations

Each successful POST appends exactly one new SvCards row. The same CardId may appear in multiple rows; existing records are never updated by this workflow.

| Endpoint | SvCardStatusId |
| --- | --- |
| OpenAccount | 1 |
| CloseAccount | 2 |
| Authorize | 3 |
| UnAuthorize | 4 |
| ClearAuthorization | 5 |
| SetTerminalStatus with status 1 | 6 (ActiveTerminal) |
| SetTerminalStatus with status 0 | 7 (InactiveTerminal) |
| QueryAuthorization | 8 |

Rows contain the trimmed request CardId, ChipId for OpenAccount only (null otherwise), the mapped status, and CreatedBy = 1. SQL Server supplies Id and CreatedDate; UpdatedBy and UpdatedDate remain null. Required lookup IDs 1 through 8 must already exist. These are operation records, so QueryAuthorization inserts a row too; they should not be interpreted as a single authoritative current-status row per card.

Invalid requests, transport errors, invalid CAS responses, and CAS results with Success = false do not insert. Success HTTP responses keep their original JSON shape. The recorder is scoped, creates a new entity, and calls SaveChangesAsync once without automatic retries. Once CAS succeeds, recording uses CancellationToken.None so a disconnected HTTP client does not cancel the database insert.

CAS and SQL Server cannot be committed atomically. If CAS succeeds but the save throws, HTTP 500 explicitly returns Success = false, CasSucceeded = true, DatabaseSaveConfirmed = false, the successful CasResponse, and a TraceId. The public message warns against automatic retries. A failed save acknowledgement can have an uncertain commit outcome, so check the database and correlated logs before manually repeating the operation. A client repeating a successful request will intentionally create another row; this schema has no idempotency key.

Validation used local mock CAS responses, an HTTP TestServer and in-memory EF persistence: all eight status mappings, audit fields, one row per success, repeated calls, CAS rejections, input validation, transport failure, database save failure, and absence of automatic CAS retries passed. No live database rows or subscriber states were changed during these tests. SQL defaults and relational mapping were verified separately when the database layer was introduced.