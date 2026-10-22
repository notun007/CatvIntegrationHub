# AddCard

`POST /AddCard`

```json
{ "cardId": "3120913711", "chipId": "000000000734ff2f" }
```

Uses the same `OpenAccountRequest` and `OpenAccountResponse` as `/OpenAccount`. Both CardId and ChipId are required strings. CardId must contain decimal digits in the range 1–4294967294; ChipId must contain exactly 16 hexadecimal characters (8 bytes).

1. Validate input using the same rules as OpenAccount. Invalid input returns OpenAccount diagnostics without executing CAS commands.
2. Attempt ClearAuthorization.
3. Call OpenAccount regardless of whether clearing succeeded, failed, timed out, or could not save its audit record.
4. Return the OpenAccount response and HTTP status, including its normal persistence diagnostics when saving an opened card cannot be confirmed.

No CloseAccount call is made. Only successful operations insert SvCards records, with CardId, ChipId, the existing status ID, and full request/response details. If both operations succeed, two rows are inserted: ClearAuthorization, followed by OpenAccount. Failed operations and invalid input create no rows. Database save failures are logged; clearing save failures do not prevent opening, and successful OpenAccount retains its existing save-failure response. ClearAuthorization never determines the AddCard result. No database migration is required.

STB.WEB provides an Add Card menu item with required Card ID and Chip ID fields, request/response preview, activity tracking, and refreshed card history after the request (clearing may have succeeded even when opening fails).
