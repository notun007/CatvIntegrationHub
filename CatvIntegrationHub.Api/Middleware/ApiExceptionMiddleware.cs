using CatvIntegrationHub.Infrastructure;

namespace CatvIntegrationHub.Api.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled API failure. TraceId: {TraceId}", context.TraceIdentifier);
            bool missingConnection = ex is MissingDatabaseConfigurationException;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            bool isOpenAccount = context.Request.Path.Equals("/OpenAccount", StringComparison.OrdinalIgnoreCase);
            bool isCloseAccount = context.Request.Path.Equals("/CloseAccount", StringComparison.OrdinalIgnoreCase);
            bool isAuthorize = context.Request.Path.Equals("/Authorize", StringComparison.OrdinalIgnoreCase);
            bool isUnAuthorize = context.Request.Path.Equals("/UnAuthorize", StringComparison.OrdinalIgnoreCase);
            bool isClearAuthorization = context.Request.Path.Equals("/ClearAuthorization", StringComparison.OrdinalIgnoreCase);
            bool isSetTerminalStatus = context.Request.Path.Equals("/SetTerminalStatus", StringComparison.OrdinalIgnoreCase);
            if (isOpenAccount || isCloseAccount || isAuthorize || isUnAuthorize || isClearAuthorization || isSetTerminalStatus || context.Request.Path.Equals("/QueryAuthorization", StringComparison.OrdinalIgnoreCase))
            {
                // Preserve the middleware's existing Error/Code/TraceId fields as well.
                CasCommandResponse diagnostic = isOpenAccount ? new OpenAccountResponse
                {
                    CommandName = "OpenAccount",
                    ReasonCode = missingConnection ? "OA500" : "OA599",
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error.",
                    HttpStatusCode = 500
                } : isCloseAccount ? new CloseAccountResponse
                {
                    CommandName = "CloseAccount",
                    ReasonCode = missingConnection ? "CA500" : "CA599",
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error.",
                    HttpStatusCode = 500
                } : isAuthorize || isUnAuthorize ? new AuthorizationCommandResponse
                {
                    CommandName = isUnAuthorize ? "UnAuthorizationDeleteAuthorization" : "Authorization",
                    ReasonCode = (isUnAuthorize ? "UA" : "AU") + (missingConnection ? "500" : "599"),
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error.",
                    HttpStatusCode = 500
                } : isClearAuthorization ? new ClearAuthorizationResponse
                {
                    CommandName = "ClearAuthorizationOfTerminal",
                    ReasonCode = missingConnection ? "CL500" : "CL599",
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error.",
                    HttpStatusCode = 500
                } : isSetTerminalStatus ? new SetTerminalStatusResponse
                {
                    CommandName = "SetTerminalStatus",
                    ReasonCode = missingConnection ? "TS500" : "TS599",
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error.",
                    HttpStatusCode = 500
                } : new QueryAuthorizationResponse
                {
                    CommandName = "QueryAuthorizationOfCertainTerminal",
                    ReasonCode = missingConnection ? "QA500" : "QA599",
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error."
                };
                var payload = System.Text.Json.JsonSerializer.SerializeToNode(diagnostic, diagnostic.GetType(),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.AsObject();
                payload["error"] = missingConnection
                    ? "Database connection is not configured. Set ConnectionStrings__CasHub for the IIS application and restart its application pool."
                    : "The API encountered an internal error. Check the server logs using the supplied trace ID.";
                payload["code"] = missingConnection ? "DatabaseConfigurationMissing" : "InternalServerError";
                payload["traceId"] = context.TraceIdentifier;
                await context.Response.WriteAsJsonAsync(payload);
                return;
            }
            await context.Response.WriteAsJsonAsync(new
            {
                Error = missingConnection
                    ? "Database connection is not configured. Set ConnectionStrings__CasHub for the IIS application and restart its application pool."
                    : "The API encountered an internal error. Check the server logs using the supplied trace ID.",
                Code = missingConnection ? "DatabaseConfigurationMissing" : "InternalServerError",
                TraceId = context.TraceIdentifier
            });
        }
    }
}
