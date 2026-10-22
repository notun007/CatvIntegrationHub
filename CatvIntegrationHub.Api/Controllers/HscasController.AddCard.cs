using CatvIntegrationHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace CatvIntegrationHub.Api.Controllers;

public sealed partial class HscasController
{
    [HttpPost("AddCard")]
    [OpenAccountDiagnosticsFilter]
    public async Task<ActionResult<OpenAccountResponse>> AddCard(OpenAccountRequest request)
    {
        // Reject invalid input with OpenAccount's own diagnostics before clearing a card.
        if (string.IsNullOrWhiteSpace(request.CardId) ||
            request.CardId.Trim().Any(c => c < '0' || c > '9') ||
            !uint.TryParse(request.CardId.Trim(), out uint id) || id == 0 || id == uint.MaxValue ||
            string.IsNullOrWhiteSpace(request.ChipId) || request.ChipId.Length != 16 ||
            request.ChipId.Any(c => !Uri.IsHexDigit(c)))
            return await OpenAccount(request);

        try
        {
            // Clearing failures (including transport and audit failures) must not stop opening.
            var cleared = _client.ClearAuthorizationOfTerminal(request.CardId);
            await ExecuteCasCommand(() => cleared,
                request.CardId, SvCardStatusEnum.ClearAuthorization, request.ChipId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddCard clearing failed; proceeding to OpenAccount. TraceId: {TraceId}",
                HttpContext?.TraceIdentifier);
        }

        var opened = _client.OpenAccount(request.CardId, request.ChipId);
        return await ExecuteCasCommand(() => opened,
            request.CardId, SvCardStatusEnum.OpenAccount, request.ChipId);

    }
}
