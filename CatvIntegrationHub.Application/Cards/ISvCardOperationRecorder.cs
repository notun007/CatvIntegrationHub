using CatvIntegrationHub.Domain.Enums;

namespace CatvIntegrationHub.Application.Cards;

public interface ISvCardOperationRecorder
{
    Task RecordAsync(string cardId, string? chipId, SvCardStatusEnum status,
        CancellationToken cancellationToken = default);
}