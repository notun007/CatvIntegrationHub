using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

// Runs before ApiController's automatic model-state response, on these actions only.
public sealed class AuthorizationDiagnosticsFilter : ActionFilterAttribute
{
    private readonly bool _unauthorize;
    public AuthorizationDiagnosticsFilter(bool unauthorize = false)
    {
        _unauthorize = unauthorize;
        Order = -3000;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool missingCard = context.ModelState.Keys.Any(k => k.Equals("CardId", StringComparison.OrdinalIgnoreCase));
        bool missingProduct = context.ModelState.Keys.Any(k => k.Equals("ProductId", StringComparison.OrdinalIgnoreCase));
        context.Result = new BadRequestObjectResult(new AuthorizationCommandResponse
        {
            CommandName = _unauthorize ? "UnAuthorizationDeleteAuthorization" : "Authorization",
            ReasonCode = (_unauthorize ? "UA" : "AU") + (missingCard ? "100" : missingProduct ? "104" : "103"),
            Reason = missingCard ? "Card ID is missing." : missingProduct ? "Product ID is missing." : "Authorization request body is missing or invalid.",
            HttpStatusCode = 400
        });
    }
}
