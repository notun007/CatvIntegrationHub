using System.ComponentModel.DataAnnotations;
using CatvIntegrationHub.Application.Cards;
using Microsoft.AspNetCore.Mvc;

namespace CatvIntegrationHub.Api.Controllers;

[ApiController]
[Route("api/SvCards")]
public sealed class SvCardsController : ControllerBase
{
    private readonly ISvCardReader _cards;
    public SvCardsController(ISvCardReader cards) => _cards = cards;

    [HttpGet]
    public async Task<ActionResult<PagedResult<SvCardDto>>> GetCards(
        [Range(1, int.MaxValue)] int page = 1, [Range(1, 100)] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if ((long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { Error = "Requested page exceeds the supported offset." });
        return Ok(await _cards.GetCardsAsync(page, pageSize, cancellationToken));
    }

    [HttpGet("history")]
    public async Task<ActionResult<PagedResult<SvCardHistoryDto>>> GetHistory(
        [FromQuery, Required, StringLength(50)] string cardId,
        [Range(1, int.MaxValue)] int page = 1, [Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _cards.GetHistoryAsync(cardId, page, pageSize, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SvCardDto>> GetCard(long id, CancellationToken cancellationToken)
    {
        var card = await _cards.GetCardAsync(id, cancellationToken);
        return card is null ? NotFound() : Ok(card);
    }
}
