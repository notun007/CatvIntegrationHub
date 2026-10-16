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
            if (context.Request.Path.Equals("/QueryAuthorization", StringComparison.OrdinalIgnoreCase))
            {
                // Preserve the middleware's existing Error/Code/TraceId fields as well.
                var query = new QueryAuthorizationResponse
                {
                    CommandName = "QueryAuthorizationOfCertainTerminal",
                    ReasonCode = missingConnection ? "QA500" : "QA599",
                    Reason = missingConnection ? "Required database configuration is missing." : "Unexpected internal error."
                };
                var payload = System.Text.Json.JsonSerializer.SerializeToNode(query,
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