using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
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
    private readonly JsonSerializerOptions _jsonOptions;

    public HscasController(HscasClient client, ISvCardOperationRecorder recorder,
        ILogger<HscasController> logger, IOptions<JsonOptions> jsonOptions)
    {
        _client = client;
        _recorder = recorder;
        _logger = logger;
        _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
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
    [OpenAccountDiagnosticsFilter]
    public Task<ActionResult<OpenAccountResponse>> OpenAccount(OpenAccountRequest request) =>
        ExecuteCasCommand(() => _client.OpenAccount(request.CardId, request.ChipId),
            request.CardId, SvCardStatusEnum.OpenAccount, request.ChipId);

    [HttpPost("CloseAccount")]
    [CloseAccountDiagnosticsFilter]
    public Task<ActionResult<CloseAccountResponse>> CloseAccount(CardRequest request) =>
        ExecuteCasCommand(() => _client.CloseAccount(request.CardId),
            request.CardId, SvCardStatusEnum.CloseAccount);

    [HttpPost("Authorize")]
    [AuthorizationDiagnosticsFilter]
    public Task<ActionResult<AuthorizationCommandResponse>> Authorize(AuthorizationRequest request) =>
        ExecuteCasCommand(() => _client.Authorization(request.CardId, request.ProductId, request.StartTime, request.EndTime),
            request.CardId, SvCardStatusEnum.Authorize,
            productId: request.ProductId, startTime: request.StartTime, endTime: request.EndTime);

    [HttpPost("UnAuthorize")]
    [AuthorizationDiagnosticsFilter(true)]
    public Task<ActionResult<AuthorizationCommandResponse>> UnAuthorize(AuthorizationRequest request) =>
        ExecuteCasCommand(() => _client.UnAuthorizationDeleteAuthorization(request.CardId, request.ProductId, request.StartTime, request.EndTime),
            request.CardId, SvCardStatusEnum.Unauthorize,
            productId: request.ProductId, startTime: request.StartTime, endTime: request.EndTime);

    [HttpPost("ClearAuthorization")]
    [ClearAuthorizationDiagnosticsFilter]
    public Task<ActionResult<ClearAuthorizationResponse>> ClearAuthorization(CardRequest request) =>
        ExecuteCasCommand(() => _client.ClearAuthorizationOfTerminal(request.CardId),
            request.CardId, SvCardStatusEnum.ClearAuthorization);

    //Asad; Commented On 16-10-2026
    //For future use: should be clear the use cases from sv then make it public
    [HttpPost("SetTerminalStatus")]
    [SetTerminalStatusDiagnosticsFilter]
    private Task<ActionResult<SetTerminalStatusResponse>> SetTerminalStatus(SetTerminalStatusRequest request) =>
        ExecuteCasCommand(() => _client.SetTerminalStatus(request.CardId, request.Status!.Value),
            request.CardId, request.Status == 1 ? SvCardStatusEnum.ActiveTerminal : SvCardStatusEnum.InactiveTerminal);

    [HttpPost("QueryAuthorization")]
    [QueryAuthorizationDiagnosticsFilter]
    public Task<ActionResult<QueryAuthorizationResponse>> QueryAuthorization(CardRequest request) =>
        ExecuteCasCommand(() => _client.QueryAuthorizationOfCertainTerminal(request.CardId),
            request.CardId, SvCardStatusEnum.QueryAuthorization);

    private async Task<ActionResult<TResponse>> ExecuteCasCommand<TResponse>(Func<TResponse> command,
        string cardId, SvCardStatusEnum status, string? chipId = null,
        string? productId = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null)
        where TResponse : CasCommandResponse
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

        if (response is SetTerminalStatusResponse terminal && terminal.HttpStatusCode != 200)
            return StatusCode(terminal.HttpStatusCode, terminal);

        if (response is ClearAuthorizationResponse clearing && clearing.HttpStatusCode != 200)
            return StatusCode(clearing.HttpStatusCode, clearing);

        if (response is AuthorizationCommandResponse authorization && authorization.HttpStatusCode != 200)
            return StatusCode(authorization.HttpStatusCode, authorization);

        if (response is CloseAccountResponse closing && closing.HttpStatusCode != 200)
            return StatusCode(closing.HttpStatusCode, closing);

        if (response is OpenAccountResponse opening && opening.HttpStatusCode != 200)
            return StatusCode(opening.HttpStatusCode, opening);

        if (response is QueryAuthorizationResponse query && query.HttpStatusCode != 200)
            return StatusCode(query.HttpStatusCode, query);

        if (!response.Success)
            return Ok(response);

        try
        {
            // Finish recording even if the HTTP caller disconnects after CAS has succeeded.
            await _recorder.RecordAsync(cardId, chipId, status, productId, startTime, endTime,
                requestHex: response.RequestHex, responseHex: response.ResponseHex,
                responseJson: JsonSerializer.Serialize(response, response.GetType(), _jsonOptions),
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAS succeeded but SvCards persistence was not confirmed for status {StatusId}. TraceId: {TraceId}",
                (short)status, HttpContext?.TraceIdentifier);
            if (response is SetTerminalStatusResponse terminalResponse)
                return StatusCode(500, new
                {
                    Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                    Success = false,
                    Status = "Failure",
                    ReasonCode = "TS501",
                    Reason = "CAS applied the terminal status, but saving the SvCards record could not be confirmed. Do not automatically retry.",
                    PersistenceReasonCode = "TS501",
                    CasSucceeded = true,
                    DatabaseSaveConfirmed = false,
                    CasResponse = terminalResponse,
                    TraceId = HttpContext?.TraceIdentifier
                });
            if (response is ClearAuthorizationResponse clearResponse)
                return StatusCode(500, new
                {
                    Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                    Success = false,
                    Status = "Failure",
                    ReasonCode = "CL501",
                    Reason = "CAS cleared the authorizations, but saving the SvCards record could not be confirmed. Do not automatically retry.",
                    PersistenceReasonCode = "CL501",
                    CasSucceeded = true,
                    DatabaseSaveConfirmed = false,
                    CasResponse = clearResponse,
                    TraceId = HttpContext?.TraceIdentifier
                });
            if (response is AuthorizationCommandResponse authorizationResponse)
            {
                string prefix = status == SvCardStatusEnum.Unauthorize ? "UA" : "AU";
                return StatusCode(500, new
                {
                    Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                    Success = false,
                    Status = "Failure",
                    ReasonCode = prefix + "501",
                    Reason = "CAS applied the command, but saving the SvCards record could not be confirmed. Do not automatically retry.",
                    PersistenceReasonCode = prefix + "501",
                    CasSucceeded = true,
                    DatabaseSaveConfirmed = false,
                    CasResponse = authorizationResponse,
                    TraceId = HttpContext?.TraceIdentifier
                });
            }
            if (response is CloseAccountResponse closeResponse)
                return StatusCode(500, new
                {
                    Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                    Success = false,
                    Status = "Failure",
                    ReasonCode = "CA501",
                    Reason = "Account closed in CAS, but saving the SvCards record could not be confirmed. Do not automatically retry.",
                    PersistenceReasonCode = "CA501",
                    CasSucceeded = true,
                    DatabaseSaveConfirmed = false,
                    CasResponse = closeResponse,
                    TraceId = HttpContext?.TraceIdentifier
                });
            if (response is OpenAccountResponse openResponse)
                return StatusCode(500, new
                {
                    Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                    Success = false,
                    Status = "Failure",
                    ReasonCode = "OA501",
                    Reason = "Account opened in CAS, but saving the SvCards record could not be confirmed. Do not automatically retry.",
                    PersistenceReasonCode = "OA501",
                    CasSucceeded = true,
                    DatabaseSaveConfirmed = false,
                    CasResponse = openResponse,
                    TraceId = HttpContext?.TraceIdentifier
                });
            if (response is QueryAuthorizationResponse queryResponse)
                return StatusCode(500, new
                {
                    Error = "CAS command succeeded, but saving the SvCards record could not be confirmed. Do not automatically retry this command.",
                    Success = false,
                    Status = queryResponse.Status,
                    ReasonCode = queryResponse.ReasonCode,
                    Reason = queryResponse.Reason,
                    PersistenceReasonCode = "QA501",
                    CasSucceeded = true,
                    DatabaseSaveConfirmed = false,
                    CasResponse = response,
                    TraceId = HttpContext?.TraceIdentifier
                });
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
