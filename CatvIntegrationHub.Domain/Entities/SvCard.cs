namespace CatvIntegrationHub.Domain.Entities;

public sealed class SvCard
{
    public long Id { get; set; }
    public string CardId { get; set; } = string.Empty;
    public string? ChipId { get; set; }
    public string? ProductId { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string? RequestHex { get; set; }
    public string? ResponseHex { get; set; }
    public short SvCardStatusId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public SvCardStatus SvCardStatus { get; set; } = null!;
}
