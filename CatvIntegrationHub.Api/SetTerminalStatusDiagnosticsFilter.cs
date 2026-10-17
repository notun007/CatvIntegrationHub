using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

public sealed class SetTerminalStatusDiagnosticsFilter : ActionFilterAttribute
{
    public SetTerminalStatusDiagnosticsFilter() { Order = -3000; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool missingCard = context.ModelState.Keys.Any(k => k.Equals("CardId", StringComparison.OrdinalIgnoreCase));
        bool invalidStatus = context.ModelState.Keys.Any(k => k.Equals("Status", StringComparison.OrdinalIgnoreCase)
            || k.Equals("$.status", StringComparison.OrdinalIgnoreCase));
        context.Result = new BadRequestObjectResult(new SetTerminalStatusResponse
        {
            CommandName = "SetTerminalStatus",
            ReasonCode = missingCard ? "TS100" : invalidStatus ? "TS104" : "TS103",
            Reason = missingCard ? "Card ID is missing." : invalidStatus
                ? "status is required and must be 0 (inactivate) or 1 (activate)."
                : "Set terminal status request body is missing or invalid.",
            HttpStatusCode = 400
        });
    }
}
