using System.Linq.Expressions;
using CatvIntegrationHub.Application.Cards;
using CatvIntegrationHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatvIntegrationHub.Infrastructure.Persistence;

internal sealed class SvCardReader : ISvCardReader
{
    private readonly CasHubDbContext _db;
    public SvCardReader(CasHubDbContext db) => _db = db;

    private static readonly Expression<Func<SvCard, SvCardDto>> Projection = card => new SvCardDto(
        card.Id, card.CardId, card.ChipId, card.SvCardStatusId, card.SvCardStatus.Name,
        card.CreatedBy, card.CreatedDate, card.UpdatedBy, card.UpdatedDate,
        card.ProductId, card.StartTime, card.EndTime, card.RequestHex, card.ResponseHex);

    public async Task<PagedResult<SvCardDto>> GetCardsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be positive and pageSize must be between 1 and 100, within the supported offset.");

        var cards = _db.SvCards.AsNoTracking();
        var total = await cards.CountAsync(cancellationToken);
        var items = await cards.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(Projection).ToListAsync(cancellationToken);
        return new PagedResult<SvCardDto>(items, total, page, pageSize);
    }

    public Task<SvCardDto?> GetCardAsync(long id, CancellationToken cancellationToken) =>
        _db.SvCards.AsNoTracking().Where(x => x.Id == id).Select(Projection).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SvCardStatusDto>> GetStatusesAsync(CancellationToken cancellationToken) =>
        await _db.SvCardStatuses.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new SvCardStatusDto(x.Id, x.Name)).ToListAsync(cancellationToken);
}
