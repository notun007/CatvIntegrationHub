# Open Account diagnostic contract

`POST /OpenAccount` retains its existing result and raw packet properties and adds
`status`, `reasonCode`, and `reason`, following QueryAuthorization's diagnostic pattern.
Only a fully validated CAS response with result `00000000` reports
`success=true`, `status="Success"`, `reasonCode="OA000"`, and
`reason="Account opened successfully."`.

HSCAS section 4.1.2 specifies a response body containing exactly a four-byte Result.
The response session, version (2), command (1B), and body length are validated
before accepting that result. OpenAccount command expiry remains send time + 1 day.

| Code | Meaning |
|---|---|
| OA000 | Account opened successfully |
| OA100 | Missing Card ID |
| OA101 | Invalid Card ID format |
| OA102 | Card ID outside 1..4294967294 |
| OA103 | Missing or invalid request body |
| OA104 | Missing Chip ID |
| OA105 | Chip ID must contain exactly 16 hexadecimal characters (8 bytes) |
| OA201 | D0000002: incorrect CAS version |
| OA202 | D0000004: unsupported command |
| OA203 | D0000005: incorrect packet length |
| OA204 | D000000A: terminal ID zero |
| OA205 | D000001E: account not opened |
| OA206 | FFFFFFFF: CAS-reported communication failure |
| OA207 | D000001B: terminal ID FFFFFFFF |
| OA208 | D000000B: incorrect expiry |
| OA209 | D000001F: incorrect chip length |
| OA210 | D0000020: incorrect chip count |
| OA299 | Other CAS rejection; inspect original resultHex |
| OA300 | Connection refused |
| OA301 | Connection timeout |
| OA302 | Host/network unreachable or network down |
| OA303 | Receive timeout |
| OA304 | Connection closed before exchange completed |
| OA305 | Connection reset/aborted |
| OA306 | Other transport failure (includes send timeout) |
| OA400 | Malformed packet/result field or incorrect response body length |
| OA401 | Session mismatch |
| OA402 | Command mismatch |
| OA403 | Response version mismatch |
| OA500 | Invalid required configuration |
| OA501 | CAS opened the account, but database save could not be confirmed |
| OA599 | Unexpected internal error; inspect traceId/server logs |

Valid CAS rejections retain HTTP 200 with `success=false` and `status="Failure"`.
Consumers must check `success`, including in HTTP 200 responses.
Input errors return 400; gateway/packet errors 502; connect/receive timeouts 504;
configuration/internal failures 500. Non-200 diagnostic responses include `error`.
Raw CAS result codes are preserved for valid result packets. No CAS result is
fabricated when no validated result was parsed; use reasonCode/status for decisions.

Only successful CAS openings are recorded, including the enriched ResponseJson.
No database migration is required. If saving fails after CAS success, HTTP 500
returns top-level `success=false`, `status="Failure"`, `reasonCode="OA501"`,
`persistenceReasonCode="OA501"`, `casSucceeded=true`, and
`databaseSaveConfirmed=false`. The nested `casResponse` retains OA000 and CAS
success. Do not automatically retry: the account has already been opened in CAS.
Transport failures also cannot prove whether CAS applied the command. No automatic
retry is performed, including for FFFFFFFF; check the account state before retrying.

Run the integration checks (loopback CAS, stub database, no live services):

```powershell
dotnet run --project tests/OpenAccount.Diagnostics/OpenAccount.Diagnostics.csproj
```
