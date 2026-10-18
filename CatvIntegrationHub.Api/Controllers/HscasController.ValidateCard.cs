using System.Text.Json;
using CatvIntegrationHub.Application.Cards;
using CatvIntegrationHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace CatvIntegrationHub.Api.Controllers;

public sealed partial class HscasController
{
    [HttpPost("ValidateCard")]
    [ValidateCardDiagnosticsFilter]
    public async Task<ActionResult<ValidateCardResponse>> ValidateCard(
        ValidateCardRequest request)
    {
        var result = new ValidateCardResponse { CardId = request.CardId.Trim(), ChipId = request.ChipId };
        var query = _client.QueryAuthorizationOfCertainTerminal(request.CardId);
        // Invalid input did not attempt CAS and cannot form a valid audit row.
        if (query.HttpStatusCode == 400)
            return StatusCode(400, FromQuery());

        await RecordValidationStep(query, SvCardStatusEnum.QueryAuthorization, request.ChipId);
        if (query.Success || query.HttpStatusCode != 200 || query.ResultDecimal != 0xD000001E)
            return StatusCode(query.HttpStatusCode, FromQuery());

        string chipId = request.ChipId;
        var opened = _client.OpenAccount(result.CardId, chipId);
        await RecordValidationStep(opened, SvCardStatusEnum.OpenAccount, opened.HttpStatusCode == 400 ? null : chipId);
        result.Source = "OpenAccount";
        result.Success = opened.Success;
        result.ReasonCode = opened.ReasonCode;
        result.Reason = opened.Reason;
        if (!opened.Success)
            return StatusCode(opened.HttpStatusCode, result);

        // Finish cleanup after a successful open even if its audit save failed or the caller
        // disconnected. Neither the close result nor its audit alters validation success.
        CloseAccountResponse closed;
        try
        {
            closed = _client.CloseAccount(result.CardId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ValidateCard cleanup failed. TraceId: {TraceId}", HttpContext?.TraceIdentifier);
            closed = new CloseAccountResponse
            {
                CommandName = "CloseAccount", ReasonCode = "CA599",
                Reason = "Unexpected cleanup failure.", HttpStatusCode = 500
            };
        }
        await RecordValidationStep(closed, SvCardStatusEnum.CloseAccount, chipId);
        return Ok(result);

        ValidateCardResponse FromQuery()
        {
            result.Success = query.Success;
            result.ReasonCode = query.ReasonCode;
            result.Reason = query.Reason;
            return result;
        }

        async Task RecordValidationStep(CasCommandResponse response, SvCardStatusEnum status,
            string? operationChipId = null)
        {
            try
            {
                await _recorder.RecordAsync(result.CardId, operationChipId, status,
                    requestHex: response.RequestHex, responseHex: response.ResponseHex,
                    responseJson: JsonSerializer.Serialize(response, response.GetType(), _jsonOptions),
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ValidateCard could not confirm SvCards save for {Status}. TraceId: {TraceId}",
                    status, HttpContext?.TraceIdentifier);
            }
        }
    }
}
