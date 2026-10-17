using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

// Run before ApiController's automatic model-state response, for OpenAccount only.
public sealed class OpenAccountDiagnosticsFilter : ActionFilterAttribute
{
    public OpenAccountDiagnosticsFilter() { Order = -3000; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool missingCard = context.ModelState.Keys.Any(k => k.Equals("CardId", StringComparison.OrdinalIgnoreCase));
        bool missingChip = context.ModelState.Keys.Any(k => k.Equals("ChipId", StringComparison.OrdinalIgnoreCase));
        context.Result = new BadRequestObjectResult(new OpenAccountResponse
        {
            CommandName = "OpenAccount",
            ReasonCode = missingCard ? "OA100" : missingChip ? "OA104" : "OA103",
            Reason = missingCard ? "Card ID is missing." : missingChip ? "Chip ID is missing." : "Open account request body is missing or invalid.",
            HttpStatusCode = 400
        });
    }
}
