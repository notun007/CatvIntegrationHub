using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

// Runs before ApiController's automatic model-state response, on this action only.
public sealed class QueryAuthorizationDiagnosticsFilter : ActionFilterAttribute
{
    public QueryAuthorizationDiagnosticsFilter() { Order = -3000; }
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool missingCard = context.ModelState.Keys.Any(k => k.Equals("CardId", StringComparison.OrdinalIgnoreCase));
        context.Result = new BadRequestObjectResult(new QueryAuthorizationResponse
        {
            CommandName = "QueryAuthorizationOfCertainTerminal",
            ReasonCode = missingCard ? "QA100" : "QA103",
            Reason = missingCard ? "Card ID is missing." : "Query request body is missing or invalid.",
            HttpStatusCode = 400
        });
    }
}
