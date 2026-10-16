using CatvIntegrationHub.Application.Cards;
using Microsoft.AspNetCore.Mvc;

namespace CatvIntegrationHub.Api.Controllers;

[ApiController]
[Route("api/SvCardStatuses")]
public sealed class SvCardStatusesController : ControllerBase
{
    private readonly ISvCardReader _cards;
    public SvCardStatusesController(ISvCardReader cards) => _cards = cards;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvCardStatusDto>>> GetStatuses(CancellationToken cancellationToken) =>
        Ok(await _cards.GetStatusesAsync(cancellationToken));
}
