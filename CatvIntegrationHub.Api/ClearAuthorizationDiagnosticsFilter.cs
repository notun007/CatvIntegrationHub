using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

// Run before ApiController's automatic model-state response, for ClearAuthorization only.
public sealed class ClearAuthorizationDiagnosticsFilter : ActionFilterAttribute
{
    public ClearAuthorizationDiagnosticsFilter() { Order = -3000; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool missingCard = context.ModelState.Keys.Any(k => k.Equals("CardId", StringComparison.OrdinalIgnoreCase));
        context.Result = new BadRequestObjectResult(new ClearAuthorizationResponse
        {
            CommandName = "ClearAuthorizationOfTerminal",
            ReasonCode = missingCard ? "CL100" : "CL103",
            Reason = missingCard ? "Card ID is missing." : "Clear authorization request body is missing or invalid.",
            HttpStatusCode = 400
        });
    }
}
