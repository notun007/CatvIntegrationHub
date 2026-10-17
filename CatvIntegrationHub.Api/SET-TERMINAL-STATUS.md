# Set Terminal Status diagnostic contract

The production SetTerminalStatus controller action remains **private** and is not
exposed as a callable HTTP endpoint. Its request model, client, controller, and
exception handling now follow the other operations' diagnostic pattern. No route
has been enabled. The test project uses a test-only adapter to exercise the
private action over HTTP, and checks that production /SetTerminalStatus returns 404.

The command retains CardId and status: 0 means inactivate, 1 means activate.
Request command is 0B, body length 000A, Send_Or_Not=1, with expiry of send time
plus 3 days for both status values (HSCAS section 4.9).

Existing CAS result and raw packet fields remain. The new diagnostic fields are
`status`, `reasonCode`, and `reason`; reason codes use the TS prefix.
Only a fully validated CAS result `00000000` reports `success=true`,
`status="Success"`, `reasonCode="TS000"`, with one of these messages:

| Requested status | Reason |
|---|---|
| 0 | Terminal inactivated successfully. |
| 1 | Terminal activated successfully. |

Response session, CAS version (2), and command (0B) must match. Section 4.9.2
defines Result (4 bytes), Emm_Count (1 byte), then Emm_Len (1 byte) and Emm_Data
for each packet. Zero EMM packets is valid. Missing counts, truncated packets,
inconsistent lengths, or trailing data prevent success. Valid CAS rejections are
reported before EMM validation.

| Code | Meaning |
|---|---|
| TS000 | Terminal activated/inactivated successfully |
| TS100 | Missing Card ID |
| TS101 | Invalid Card ID format |
| TS102 | Card ID outside 1..4294967294 |
| TS103 | Missing or invalid request body |
| TS104 | Missing/invalid status; must be integer 0 or 1 |
| TS201 | D0000002: incorrect CAS version |
| TS202 | D0000004: unsupported command |
| TS203 | D0000005: incorrect packet length |
| TS204 | D000000A: terminal ID zero |
| TS205 | D000001E: account not opened |
| TS206 | FFFFFFFF: CAS-reported communication failure |
| TS207 | D000001B: terminal ID FFFFFFFF |
| TS208 | D000000B: incorrect expiry |
| TS209 | D000000F: incorrect terminal status |
| TS299 | Other CAS rejection; inspect resultHex |
| TS300 | Connection refused |
| TS301 | Connection timeout |
| TS302 | Host/network unreachable or network down |
| TS303 | Receive timeout |
| TS304 | Connection closed before exchange completed |
| TS305 | Connection reset/aborted |
| TS306 | Other transport failure (includes send timeout) |
| TS400 | Malformed packet or missing/incomplete result field |
| TS401 | Session mismatch |
| TS402 | Command mismatch |
| TS403 | Response version mismatch |
| TS404 | Missing EMM count, inconsistent EMM lengths/count, or trailing data |
| TS500 | Invalid required configuration |
| TS501 | CAS applied the status, but database save could not be confirmed |
| TS599 | Unexpected internal error; inspect traceId/server logs |

Controller diagnostic responses follow the established HTTP-status convention:
valid CAS rejections retain HTTP 200 with `success=false`, `status="Failure"`;
input errors use 400; gateway/packet errors 502; connect/receive timeouts 504;
configuration/internal failures 500. The response filter handles invalid model
state before ApiController's default response. This is ready for future activation;
the production route remains unavailable. Non-200 diagnostic responses include `error`.
Raw CAS result codes are preserved when parsed from responses with matching
headers; zero Result with invalid EMM data remains failure. No result is fabricated
for transport failures or invalid headers.

Only validated CAS success is recorded: ActiveTerminal for status=1,
InactiveTerminal for status=0, including enriched ResponseJson. No migration is
required. If saving fails, HTTP 500 returns `success=false`, `status="Failure"`,
TS501 in `reasonCode` and `persistenceReasonCode`, `casSucceeded=true`, and
`databaseSaveConfirmed=false`. Nested casResponse preserves TS000 and CAS success.
Do not automatically retry a command already applied by CAS. Communication
failures, including FFFFFFFF, are not automatically retried; verify terminal state
when the outcome is uncertain.

Run the combined integration checks (loopback CAS and stub database, no live CAS):

```powershell
dotnet run --project tests/OpenAccount.Diagnostics/OpenAccount.Diagnostics.csproj
```
