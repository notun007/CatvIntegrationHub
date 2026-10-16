namespace CatvIntegrationHub.Domain.Enums;

/// <summary>Matches the IDs in dbo.SvCardStatuses; these are not CAS terminal status bytes.</summary>
public enum SvCardStatusEnum : short
{
    OpenAccount = 1,
    CloseAccount = 2,
    Authorize = 3,
    Unauthorize = 4,
    ClearAuthorization = 5,
    ActiveTerminal = 6,
    InactiveTerminal = 7,
    QueryAuthorization = 8
}