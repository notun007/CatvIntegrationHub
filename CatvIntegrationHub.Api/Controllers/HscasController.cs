using System.Net.Sockets;
using CatvIntegrationHub.Application.Cards;
using CatvIntegrationHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace CatvIntegrationHub.Api.Controllers;

[ApiController]
[Route("")]
public sealed class HscasController : ControllerBase
{
    private readonly HscasClient _client;
    private readonly ISvCardOperationRecorder _recorder;
    private readonly ILogger<HscasController> _logger;

    public HscasController(HscasClient client, ISvCardOperationRecorder recorder,
        ILogger<HscasController> logger)
    {
        _client = client;
        _recorder = recorder;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult GetServiceInfo()
    {
        return Ok(new
        {
            Service = "Somavission HSCAS API",
            Endpoints = new[]
            {
                "POST /OpenAccount",
                "POST /CloseAccount",
                "POST /Authorize",
                "POST /UnAuthorize",
                "POST /ClearAuthorization",
                "POST /SetTerminalStatus",
                "POST /QueryAuthorization"
            }
        });
    }

    [HttpPost("OpenAccount")]
    public Task<ActionResult<CasCommandResponse>> OpenAccount(OpenAccountRequest request) =>
        ExecuteCasCommand(() => _client.OpenAccount(request.CardId, request.ChipId),
            request.CardId, SvCardStatusEnum.OpenAccount, request.ChipId);

    [HttpPost("CloseAccount")]
    public Task<ActionResult<CasCommandResponse>> CloseAccount(CardRequest request) =>
        ExecuteCasCommand(() => _client.CloseAccount(request.CardId),
            request.CardId, SvCardStatusEnum.CloseAccount);

    [HttpPost("Authorize")]
    public Task<ActionResult<CasCommandResponse>> Authorize(AuthorizationRequest request) =>
        ExecuteCasCommand(() => _client.Authorization(request.CardId, request.ProductId, request.StartTime, request.EndTime),
            request.CardId, SvCardStatusEnum.Authorize);

    [HttpPost("UnAuthorize")]
    public Task<ActionResult<CasCommandResponse>> UnAuthorize(AuthorizationRequest request) =>
        ExecuteCasCommand(() => _client.UnAuthorizationDeleteAuthorization(request.CardId, request.ProductId, request.StartTime, request.EndTime),
            request.CardId, SvCardStatusEnum.Unauthorize);

    [HttpPost("ClearAuthorization")]
    public Task<ActionResult<CasCommandResponse>> ClearAuthorization(CardRequest request) =>
        ExecuteCasCommand(() => _client.ClearAuthorizationOfTerminal(request.CardId),
            request.CardId, SvCardStatusEnum.ClearAuthorization);

    [HttpPost("SetTerminalStatus")]
    public Task<ActionResult<CasCommandResponse>> SetTerminalStatus(SetTerminalStatusRequest request) =>
        ExecuteCasCommand(() => _client.SetTerminalStatus(request.CardId, request.Status!.Value),
            request.CardId, request.Status == 1 ? SvCardStatusEnum.ActiveTerminal : SvCardStatusEnum.InactiveTerminal);

    [HttpPost("QueryAuthorization")]
    public Task<ActionResult<QueryAuthorizationResponse>> QueryAuthorization(CardRequest request) =>
        ExecuteCasCommand(() => _client.QueryAuthorizationOfCertainTerminal(request.CardId),
            request.CardId, SvCardStatusEnum.QueryAuthorization);

    private async Task<ActionResult<TResponse>> ExecuteCasCommand<TResponse>(Func<TResponse> command,
        string cardId, SvCardStatusEnum status, string? chipId = null) where TResponse : CasCommandResponse
    {
        TResponse response;
        try
        {
            response = command();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (FormatException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Error = ex.Message });
        }
        catch (IOException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Error = ex.Message });
        }
        catch (SocketException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Error = ex.Message });
        }

        if (!response.Success)
            return Ok(response);

        try
        {
            // Finish recording even if the HTTP caller disconnects after CAS has succeeded.
            await _recorder.RecordAsync(cardId, chipId, status, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAS succeeded but SvCards persistence was not confirmed for status {StatusId}. TraceId: {TraceId}",
                (short)status, HttpContext?.TraceIdentifier);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                Success = false,
                CasSucceeded = true,
                DatabaseSaveConfirmed = false,
                CasResponse = response,
                TraceId = HttpContext?.TraceIdentifier
            });
        }

        return Ok(response);
    }
}