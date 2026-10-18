using System.ComponentModel.DataAnnotations;

namespace CatvIntegrationHub.Api;

public sealed record ValidateCardRequest(
    [Required] string CardId,
    [Required, RegularExpression("^[0-9a-fA-F]{16}$", ErrorMessage = "Chip ID must contain exactly 16 hexadecimal characters (8 bytes).")]
    string ChipId);

public sealed class ValidateCardResponse
{
    public bool Success { get; set; }
    public string Status => Success ? "Success" : "Failure";
    public string CardId { get; set; } = string.Empty;
    public string? ChipId { get; set; }
    public string Source { get; set; } = "QueryAuthorization";
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
