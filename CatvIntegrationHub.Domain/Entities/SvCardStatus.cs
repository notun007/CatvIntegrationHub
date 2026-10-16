namespace CatvIntegrationHub.Domain.Entities;

public sealed class SvCardStatus
{
    public short Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<SvCard> SvCards { get; set; } = new List<SvCard>();
}
