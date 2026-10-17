using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

// Run before ApiController's automatic model-state response, for CloseAccount only.
public sealed class CloseAccountDiagnosticsFilter : ActionFilterAttribute
{
    public CloseAccountDiagnosticsFilter() { Order = -3000; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool missingCard = context.ModelState.Keys.Any(k => k.Equals("CardId", StringComparison.OrdinalIgnoreCase));
        context.Result = new BadRequestObjectResult(new CloseAccountResponse
        {
            CommandName = "CloseAccount",
            ReasonCode = missingCard ? "CA100" : "CA103",
            Reason = missingCard ? "Card ID is missing." : "Close account request body is missing or invalid.",
            HttpStatusCode = 400
        });
    }
}
