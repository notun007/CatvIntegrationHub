namespace CatvIntegrationHub.Application.Cards;

public sealed record SvCardDto(long Id, string CardId, string? ChipId,
    short SvCardStatusId, string StatusName, int CreatedBy, DateTime CreatedDate,
    int? UpdatedBy, DateTime? UpdatedDate,
    string? ProductId, DateTimeOffset? StartTime, DateTimeOffset? EndTime,
    string? RequestHex, string? ResponseHex, string? ResponseJson);

public sealed record SvCardHistoryDto(long Id, string CardId, short SvCardStatusId, string StatusName,
    int CreatedBy, DateTime CreatedDate, string? ProductId, DateTimeOffset? StartTime, DateTimeOffset? EndTime);

public sealed record SvCardStatusDto(short Id, string Name);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public interface ISvCardReader
{
    Task<PagedResult<SvCardDto>> GetCardsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<PagedResult<SvCardHistoryDto>> GetHistoryAsync(string cardId, int page, int pageSize, CancellationToken cancellationToken);
    Task<SvCardDto?> GetCardAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SvCardStatusDto>> GetStatusesAsync(CancellationToken cancellationToken);
}
