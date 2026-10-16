using CatvIntegrationHub.Domain.Enums;

namespace CatvIntegrationHub.Application.Cards;

public interface ISvCardOperationRecorder
{
    Task RecordAsync(string cardId, string? chipId, SvCardStatusEnum status,
        string? productId = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null,
        string? requestHex = null, string? responseHex = null,
        CancellationToken cancellationToken = default);
}