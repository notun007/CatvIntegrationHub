# Clear Authorization diagnostic contract

`POST /ClearAuthorization` retains its request, CAS result fields, raw packets,
and command name `ClearAuthorizationOfTerminal`. It adds `status`, `reasonCode`,
and `reason`, following the other account and authorization endpoints.

Only a fully validated CAS response with result `00000000` reports
`success=true`, `status="Success"`, `reasonCode="CL000"`, and
`reason="All authorizations cleared successfully."`.

The request retains command 1F, a five-byte body, and Send_Or_Not=1.
Response session, CAS version (2), and command (1F) must match. HSCAS section
4.4.2 specifies exactly a four-byte Result field; missing, truncated, or extra
body bytes prevent success. No EMM or returned-product count is expected.

| Code | Meaning |
|---|---|
| CL000 | All authorizations cleared successfully |
| CL100 | Missing Card ID |
| CL101 | Invalid Card ID format |
| CL102 | Card ID outside 1..4294967294 |
| CL103 | Missing or invalid request body |
| CL201 | D0000002: incorrect CAS version |
| CL202 | D0000004: unsupported command |
| CL203 | D0000005: incorrect packet length |
| CL204 | D000000A: terminal ID zero |
| CL205 | D000001E: account not opened |
| CL206 | FFFFFFFF: CAS-reported communication failure |
| CL207 | D000001B: terminal ID FFFFFFFF |
| CL299 | Other CAS rejection; inspect resultHex |
| CL300 | Connection refused |
| CL301 | Connection timeout |
| CL302 | Host/network unreachable or network down |
| CL303 | Receive timeout |
| CL304 | Connection closed before exchange completed |
| CL305 | Connection reset/aborted |
| CL306 | Other transport failure (includes send timeout) |
| CL400 | Malformed packet/result field or incorrect response body length |
| CL401 | Session mismatch |
| CL402 | Command mismatch |
| CL403 | Response version mismatch |
| CL500 | Invalid required configuration |
| CL501 | CAS cleared the authorizations, but database save could not be confirmed |
| CL599 | Unexpected internal error; inspect traceId/server logs |

Valid CAS rejections retain HTTP 200 with `success=false`, `status="Failure"`.
Consumers must check success even for HTTP 200. Input errors return 400;
gateway/packet errors 502; connect/receive timeouts 504; configuration/internal
failures 500. Non-200 diagnostic responses include `error`.
Original CAS results are preserved for fully validated result packets. No CAS
result is fabricated for transport failures, invalid headers, or incorrect body lengths.

Only validated CAS success is recorded as a ClearAuthorization operation, with
the enriched ResponseJson. No migration is required. If recording fails after
CAS success, HTTP 500 returns `success=false`, `status="Failure"`, CL501 in
`reasonCode` and `persistenceReasonCode`, `casSucceeded=true`, and
`databaseSaveConfirmed=false`. Nested `casResponse` retains CL000 and CAS success.
Do not automatically retry a command already confirmed by CAS. No automatic
retry is performed for communication failures, including FFFFFFFF; verify
authorization state when the outcome is uncertain.

Run the combined account/authorization integration checks using a loopback CAS
simulator and stub database:

```powershell
dotnet run --project tests/OpenAccount.Diagnostics/OpenAccount.Diagnostics.csproj
```
