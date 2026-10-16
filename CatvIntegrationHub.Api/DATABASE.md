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

Both tables must already exist in `CasHub`. Startup never calls `Migrate`, `EnsureCreated`, or `EnsureDeleted`. Startup never applies migrations automatically. Reviewed EF migrations can be applied explicitly as described below.

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
## Authorization details and migrations

SvCards now includes nullable ProductId (varchar(50)), StartTime (datetimeoffset(7)) and EndTime (datetimeoffset(7)). Authorize and UnAuthorize store the client-supplied values after CAS succeeds. Other operation rows leave these columns null. Read DTOs include the fields. UnAuthorize records the original request dates even though its CAS packet uses the end time for both protocol timestamps. DateTimeOffset storage preserves the supplied offset.

Migrations live in CatvIntegrationHub.Infrastructure/Persistence/Migrations. ExistingCasHubBaseline is intentionally a no-op for pre-existing tables and must not be used to provision an empty database. AddAuthorizationDetailsToSvCards adds only the three nullable columns. The corresponding model snapshots are checked in. The database receives an __EFMigrationsHistory table; the two existing SvCards rows were verified unchanged with null authorization details after applying the migrations on 2026-10-16.

From the solution directory, use the pinned local EF Core 6 tool:

```powershell
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef migrations list --project CatvIntegrationHub.Infrastructure --startup-project CatvIntegrationHub.Api
dotnet ef database update --project CatvIntegrationHub.Infrastructure --startup-project CatvIntegrationHub.Api
```

The target connection comes from the usual configuration providers. A reviewed, idempotent deployment script is available at DatabaseScripts/AddAuthorizationDetailsToSvCards.sql. It is already applied to the supplied CasHub database. On another environment, confirm the two original tables exist and run the script against the correct CasHub database before deploying the updated API. Applying it again skips recorded migrations. Reverting the additive migration drops the new columns and their contents; no rollback is performed automatically.

Local mock-CAS/HTTP/in-memory-EF checks verified the new values for both authorization operations, offset preservation, null fields for other active endpoints, read DTO output, failure behavior and the migration operations. The existing private SetTerminalStatus action was preserved. No live authorization commands or test card inserts were issued.

EF migration management reference: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing
## CAS packet hex recording

SvCards.RequestHex and SvCards.ResponseHex are nullable varchar(max) columns. Each successful CAS operation passes the actual CasCommandResponse.RequestHex and ResponseHex strings to the recorder, saving them unchanged in the same row as its status and authorization details. They are not reconstructed from request inputs. Card list and detail DTOs include both fields.

Migration AddPacketHexToSvCards adds only these two columns. It was applied to CasHub on 2026-10-16; all four existing rows were preserved with null packet fields. DatabaseScripts/AddPacketHexToSvCards.sql is an idempotent incremental script for databases already baselined with AddAuthorizationDetailsToSvCards. There is no automatic startup migration or backfill of historical packet data.

Successful-command-only recording, CreatedBy = 1, partial persistence failure responses and the existing private SetTerminalStatus action remain unchanged. Rejected CAS commands and transport failures still do not create rows. Local HTTP/mock CAS/in-memory EF checks verified exact packet persistence, 20,000-character values, nullable varchar(max) mappings, DTO output and existing error behavior. No live CAS commands or test data inserts were issued.
## Full CAS response JSON

SvCards.ResponseJson is a nullable nvarchar(max) column. Successful CAS POST operations serialize the original response object using the API's configured MVC JsonSerializerOptions and its runtime type, preserving camelCase properties and derived QueryAuthorization fields such as authorizations. The JSON is saved in the same SvCards row as the packet hex and authorization details. Card list and detail DTOs expose responseJson as a JSON string. HTTP response bodies themselves retain their existing shape.

The successful-command-only rule still applies: rejected CAS commands, transport failures and validation errors do not create rows. The stored value is the successful CAS response body, not a later database-save error wrapper, HTTP headers or status code. Existing records are left null; historical response objects are not reconstructed from hex.

Migration AddResponseJsonToSvCards adds only ResponseJson and was applied to CasHub on 2026-10-16. DatabaseScripts/AddResponseJsonToSvCards.sql is an incremental idempotent script for databases already at AddPacketHexToSvCards. Startup does not automatically migrate. Deploy the rebuilt API after the schema is ready; retain the IIS database connection configuration.

Local HTTP/mock CAS/in-memory EF checks verified that stored responseJson exactly matches each active endpoint's HTTP JSON, including a nonempty authorization collection, camelCase settings, long Unicode content, DTO projection and existing failure behavior. No live CAS commands or test inserts were issued. The private SetTerminalStatus action remains private.
## Card history

GET /api/SvCards/history?cardId=3120913711&page=1&pageSize=20 returns an exact CardId match, newest CreatedDate first with Id descending as a deterministic tie-breaker. The required decimal card ID must be between 1 and 4294967294 and at most 50 characters. Whitespace is trimmed. Pagination is bounded to 1-100 rows per page and rejects unsupported offsets. An unknown card returns an empty successful page, not a CAS request.

History summaries contain Id, CardId, status ID/name, CreatedBy, CreatedDate, ProductId, StartTime and EndTime. Large packet fields and ResponseJson are omitted. GET /api/SvCards/{id} supplies saved details on demand. Queries use AsNoTracking and perform filtering, ordering and paging in SQL. No table changes, migrations, automatic index creation or CAS commands were needed for this feature. A composite CardId/CreatedDate/Id index can be considered when history volume warrants it.

The shared Angular component under Stb.Web/src/app/card-history appears on OpenAccount, CloseAccount, Authorize, UnAuthorize, ClearAuthorization, SetTerminalStatus and QueryAuthorization. It shows all saved operation types for the current card, debounces typing by 400 ms, cancels obsolete requests, clears old details on card changes, supports pagination/refresh and reloads page 1 after a successful command for the same card. The existing private terminal-status API action was preserved.

History dates retain SQL server wall-clock time because datetime stores no offset. Authorization periods are rendered in Asia/Dhaka. Legacy null hex/JSON fields display Not recorded. Errors have a separate history state and never replace the command response. There is no history for failed CAS commands under the existing successful-only recording policy.

Verification: API HTTP/in-memory EF checks covered exact matching, no prefix matches, newest-first tie ordering, pagination, invalid parameters and lightweight responses. Chrome/Playwright checks exercised all seven screens, lazy details, null historical values, empty/invalid cards, slow obsolete requests, error/retry, successful-command refresh and desktop/mobile layouts (1440x1050 and 390x844). A local API also read live CasHub history and details successfully without issuing CAS commands or inserting rows. Browser plugin was unavailable; the existing Playwright runtime was used. Builds passed. Deploy both API publish output and Angular dist/stb-web/browser, preserving IIS connection configuration. No database migration is required.