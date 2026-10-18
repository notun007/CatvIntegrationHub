using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CatvIntegrationHub.Api;

public sealed class ValidateCardDiagnosticsFilter : ActionFilterAttribute
{
    public ValidateCardDiagnosticsFilter() => Order = -3000;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        bool chipError = context.ModelState.Any(entry =>
            entry.Key.Equals("ChipId", StringComparison.OrdinalIgnoreCase) && entry.Value!.Errors.Count > 0);
        context.Result = new BadRequestObjectResult(new ValidateCardResponse
        {
            ReasonCode = chipError ? "VC104" : "VC100",
            Reason = chipError ? "Chip ID is required and must contain exactly 16 hexadecimal characters (8 bytes)." : "A valid request body with CardId and ChipId is required."
        });
    }
}
