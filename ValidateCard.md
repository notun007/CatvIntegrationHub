# ValidateCard

`POST /ValidateCard`

```json
{ "cardId": "2147483649", "chipId": "00000A00010A0C00" }
```

Both `cardId` and `chipId` are mandatory for every request, including cards that are already open. ChipId must contain exactly 16 hexadecimal characters (8 bytes). Missing, blank, null, or malformed ChipId returns HTTP 400 with `success: false` before any CAS operation. No saved-ChipId fallback is used.

Sequence:

1. QueryAuthorization succeeds (account open): return success immediately, including empty authorization lists.
2. QueryAuthorization reports account not opened (`D000001E`): call OpenAccount. Other query failures stop validation.
3. OpenAccount fails: return failure and do not close.
4. OpenAccount succeeds: call CloseAccount, then return success and the OpenAccount information regardless of the close result.

Responses contain only `success`, `status`, `cardId`, `chipId`, `source`, `reasonCode`, and `reason`. The source is QueryAuthorization or OpenAccount. Reason fields come from that operation and are never replaced by the close result. Detailed CAS responses are saved in SvCards rather than returned by ValidateCard.

Each executed step inserts a separate SvCards row using its existing status ID, request/response hex, and response JSON. Failed operations are included. Invalid CardId, ChipId, or body requests do not attempt CAS or create rows.

Audit saves and cleanup continue after HTTP disconnection. Audit save failures are logged on the server, while `success` remains the CAS validation result. Do not blindly retry a validation to repair an audit save. No new database migration is required.
