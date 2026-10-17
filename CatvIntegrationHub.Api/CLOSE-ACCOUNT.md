# Close Account diagnostic contract

`POST /CloseAccount` retains existing result and raw packet fields and adds
`status`, `reasonCode`, and `reason`, following OpenAccount and QueryAuthorization.
Only a fully validated CAS response with result `00000000` reports
`success=true`, `status="Success"`, `reasonCode="CA000"`, and
`reason="Account closed successfully."`.

The request retains command 15, Is_Send=1, and expiry of send time + 1 day.
Response session, version (2), and command (15) must match the request.
Per HSCAS section 4.2.2, a successful response must contain Result (4 bytes),
Emm_Count (1 byte), then Emm_Len (1 byte) and Emm_Data for each EMM packet.
The entire EMM structure must match the response length, with no trailing data.
CAS rejections with a valid four-byte Result are reported before EMM validation.

| Code | Meaning |
|---|---|
| CA000 | Account closed successfully |
| CA100 | Missing Card ID |
| CA101 | Invalid Card ID format |
| CA102 | Card ID outside 1..4294967294 |
| CA103 | Missing or invalid request body |
| CA201 | D0000002: incorrect HSCAS version |
| CA202 | D0000004: unsupported command |
| CA203 | D0000005: incorrect packet length |
| CA204 | D000000A: terminal ID zero |
| CA205 | D000001E: account not opened |
| CA206 | FFFFFFFF: CAS-reported communication failure |
| CA207 | D000001B: terminal ID FFFFFFFF |
| CA208 | D000000B: incorrect expiry |
| CA299 | Other CAS rejection; inspect resultHex |
| CA300 | Connection refused |
| CA301 | Connection timeout |
| CA302 | Host/network unreachable or network down |
| CA303 | Receive timeout |
| CA304 | Connection closed before exchange completed |
| CA305 | Connection reset/aborted |
| CA306 | Other transport failure (includes send timeout) |
| CA400 | Malformed packet or missing/incomplete result field |
| CA401 | Session mismatch |
| CA402 | Command mismatch |
| CA403 | Response version mismatch |
| CA404 | Missing EMM count, inconsistent EMM lengths/count, or trailing EMM data |
| CA500 | Invalid required configuration |
| CA501 | CAS closed the account, but database save could not be confirmed |
| CA599 | Unexpected internal error; inspect traceId/server logs |

Valid CAS rejections retain HTTP 200 with `success=false`, `status="Failure"`.
Consumers must check success even for HTTP 200. Input errors return 400;
gateway/packet errors 502; connect/receive timeouts 504; configuration/internal
failures 500. Non-200 diagnostic responses include `error`.
Original CAS results are preserved when parsed from a response with matching
headers. A zero result accompanied by malformed EMM data remains a failure.
No CAS result is fabricated for transport failures or invalid headers.

Only validated CAS successes are recorded as CloseAccount operations, including
the enriched ResponseJson. No migration is required. If recording fails after CAS
success, HTTP 500 returns top-level `success=false`, `status="Failure"`,
`reasonCode="CA501"`, `persistenceReasonCode="CA501"`, `casSucceeded=true`,
and `databaseSaveConfirmed=false`. The nested casResponse retains CA000.
Do not automatically retry an account closure that CAS has already confirmed.
No automatic retry is performed for communication failures, including FFFFFFFF;
verify account state before retrying when the outcome is uncertain.

Run the combined OpenAccount/CloseAccount integration checks with a local CAS
simulator and stub database:

```powershell
dotnet run --project tests/OpenAccount.Diagnostics/OpenAccount.Diagnostics.csproj
```
