# Authorization and Un-authorization diagnostic contracts

`POST /Authorize` and `POST /UnAuthorize` retain their existing request fields,
raw packets, CAS result fields, and command names. Both add `status`, `reasonCode`,
and `reason`, following OpenAccount, CloseAccount, and QueryAuthorization.

Authorize uses the `AU` prefix; UnAuthorize uses `UA`. Only a fully validated CAS
response with result `00000000` reports `success=true`, `status="Success"`, and:

| Endpoint | Code | Reason |
|---|---|---|
| /Authorize | AU000 | Product authorized successfully. |
| /UnAuthorize | UA000 | Product unauthorized successfully. |

Both operations use command 01. Existing encoding is preserved: Authorize sends
the requested startTime/endTime; UnAuthorize sends begin=end=requested endTime
to delete the authorization, per HSCAS section 4.3. Both continue requiring the
input endTime to be later than startTime and the current time. Card ID must be
decimal in 1..4294967294; product ID must be nonzero and fit in two bytes (decimal,
0x-prefixed hexadecimal, or hexadecimal containing letters). Input times must fit
unsigned 32-bit Unix seconds to keep the command packet valid.

Response session, CAS version (2), and command (01) must match. HSCAS section
4.3.2 defines Result (4 bytes), ReturnAmount (1 byte), then ProductNumber (2 bytes),
Desc_Len (1 byte), and Description for each returned product. Zero returned
products is valid. Missing counts, truncated records/descriptions, or trailing
data prevent success. CAS rejections with valid Result are reported before
returned-product validation. Original response packets remain available.

For every suffix below, prepend AU or UA according to the endpoint:

| Suffix | Meaning |
|---|---|
| 000 | Command succeeded |
| 100 | Missing Card ID |
| 101 | Invalid Card ID format |
| 102 | Card ID outside 1..4294967294 |
| 103 | Missing or invalid request body, including invalid date formats |
| 104 | Missing Product ID |
| 105 | Invalid or zero Product ID, or value outside two-byte range |
| 106 | endTime must be later than startTime |
| 107 | endTime must be later than the current time |
| 108 | Input dates outside unsigned 32-bit Unix seconds, including missing startTime |
| 201 | D0000002: incorrect CAS version |
| 202 | D0000004: unsupported command |
| 203 | D0000005: incorrect packet length |
| 204 | D000000A: terminal ID zero |
| 205 | D000001E: account not opened |
| 206 | FFFFFFFF: CAS-reported communication failure |
| 207 | D000001B: terminal ID FFFFFFFF |
| 211 | D0000022: product ID zero (also listed in section 4.3.2) |
| 299 | Other CAS rejection; inspect resultHex |
| 300 | Connection refused |
| 301 | Connection timeout |
| 302 | Host/network unreachable or network down |
| 303 | Receive timeout |
| 304 | Connection closed before exchange completed |
| 305 | Connection reset/aborted |
| 306 | Other transport failure (includes send timeout) |
| 400 | Malformed packet or missing/incomplete result field |
| 401 | Session mismatch |
| 402 | Command mismatch |
| 403 | Response version mismatch |
| 404 | Missing returned-product count, truncated records/descriptions, or trailing data |
| 500 | Invalid required configuration |
| 501 | CAS applied the command, but database save could not be confirmed |
| 599 | Unexpected internal error; inspect traceId/server logs |

Valid CAS rejections retain HTTP 200 with `success=false`, `status="Failure"`.
Consumers must check success even for HTTP 200. Input errors return 400;
gateway/packet errors 502; connect/receive timeouts 504; configuration/internal
failures 500. Non-200 diagnostic responses include `error`. A parsed zero result
with malformed returned-product data remains a failure. No CAS result is
fabricated for transport failures or invalid headers.

Only validated CAS success is recorded, under the corresponding Authorize or
Unauthorize operation status, with the enriched ResponseJson. No migration is
required. If saving fails after CAS success, HTTP 500 returns `success=false`,
`status="Failure"`, AU501/UA501 in `reasonCode` and `persistenceReasonCode`,
`casSucceeded=true`, and `databaseSaveConfirmed=false`. Nested `casResponse`
retains the original CAS success. Do not automatically retry a command already
confirmed by CAS. No automatic retry is performed for communication failures,
including FFFFFFFF; verify account/authorization state when the outcome is uncertain.

Run the integration checks for all four account/authorization operations using a
loopback CAS simulator and stub database:

```powershell
dotnet run --project tests/OpenAccount.Diagnostics/OpenAccount.Diagnostics.csproj
```
