# Query Authorization diagnostic contract

Only POST /QueryAuthorization adds `status`, `reasonCode`, and `reason`.
All existing response properties, authorization records and raw CAS result values remain.
`status` is Success only for a fully validated CAS success packet, regardless of whether its authorization list is empty.
A failed connection cannot establish whether a card is registered or authorized. No automatic retry is performed.

| Code | Meaning |
|---|---|
| QA000 | Account open with at least one authorization period (begin < end); check dates for current validity |
| QA001 | Account open with no authorization periods: empty response or only un-authorization records (begin >= end) |
| QA100 | Missing Card ID |
| QA101 | Invalid Card ID format |
| QA102 | Card ID outside 1..4294967294 |
| QA103 | Missing or invalid request body |
| QA201 | D0000002: incorrect CAS version |
| QA202 | D0000004: unsupported command |
| QA203 | D0000005: incorrect packet length |
| QA204 | D000000A: terminal ID zero |
| QA205 | D000001E: account not opened |
| QA206 | FFFFFFFF: CAS-reported communication failure |
| QA207 | D000001B: terminal ID FFFFFFFF |
| QA208 | D000000B: incorrect expiry |
| QA209 | D000001F: incorrect chip length |
| QA210 | D0000020: incorrect chip count |
| QA211 | D0000022: product ID zero |
| QA299 | Other CAS rejection; inspect original resultHex |
| QA300 | Connection refused |
| QA301 | Connection timeout |
| QA302 | Host/network unreachable or network down |
| QA303 | Receive timeout |
| QA304 | Connection closed before exchange completed |
| QA305 | Connection reset/aborted |
| QA306 | Other transport failure (includes send timeout) |
| QA400 | Malformed packet/result field |
| QA401 | Session mismatch |
| QA402 | Command mismatch |
| QA403 | Response version mismatch |
| QA404 | Missing count or authorization length mismatch |
| QA500 | Missing/invalid required configuration |
| QA599 | Unexpected internal error; inspect traceId/server logs |

Valid CAS rejections retain HTTP 200 and success=false. Local input errors return 400; gateway/packet errors 502; connect/receive timeouts 504; configuration/internal failures 500. Consumers must read error bodies as well as HTTP 200 bodies.
No CAS code is fabricated when no valid result was parsed: existing resultHex remains empty and resultDecimal retains its existing default. Use reasonCode/status for decisions.

Successful queries are recorded as before, including the enriched ResponseJson. No schema migration is needed.
If recording fails after CAS success, the existing HTTP 500 envelope and its Success=false, CasSucceeded, DatabaseSaveConfirmed, CasResponse and TraceId are preserved. Additive top-level status/reasonCode/reason describe the successful CAS query; persistenceReasonCode=QA501 describes the separate unconfirmed save. The nested CasResponse also retains all fields. Do not treat envelope success=false as proof the card/CAS query failed.

HSCAS section 4.3 distinguishes authorization (begin < end) from un-authorization (begin >= end). Query classification uses this rule instead of ReturnAmount alone. Raw ReturnAmount, Authorizations, and hex fields are unchanged, including retained un-authorization records. Mixed responses use QA000 if any authorization period exists. QA000 does not guarantee a currently active entitlement: future and expired periods must be evaluated against the current time.

Each authorization record also includes authorizationStatus: Authorized when beginUtc < endUtc; Unauthorized when beginUtc >= endUtc. All existing fields remain. This identifies the record type, not whether it is currently active. Saved ResponseJson for new successful queries includes this field; historical JSON is unchanged.
