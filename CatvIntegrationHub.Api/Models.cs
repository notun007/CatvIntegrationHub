namespace CatvIntegrationHub.Api;

public sealed record OpenAccountRequest(string CardId, string ChipId);

public sealed record CardRequest(string CardId);

public sealed record SetTerminalStatusRequest(
    string CardId,
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.Range(0, 1)] int? Status);

public sealed record AuthorizationRequest(
    string CardId,
    string ProductId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);

public sealed class HscasOptions
{
    public string IpAddress { get; set; } = "172.16.16.50";
    public int Port { get; set; } = 8000;
    public int SendTimeoutMilliseconds { get; set; } = 10000;
    public int ReceiveTimeoutMilliseconds { get; set; } = 10000;
}

public class CasCommandResponse
{
    public string CommandName { get; set; } = string.Empty;
    public string RequestHex { get; set; } = string.Empty;
    public string ResponseHex { get; set; } = string.Empty;
    public string BodyHex { get; set; } = string.Empty;
    public string ResultHex { get; set; } = string.Empty;
    public uint ResultDecimal { get; set; }
    public string ResultMeaning { get; set; } = string.Empty;
    public bool Success { get; set; }
}

public sealed class QueryAuthorizationResponse : CasCommandResponse
{
    public ushort ReturnAmount { get; set; }
    public List<AuthorizationRecord> Authorizations { get; set; } = new();
}

public sealed class AuthorizationRecord
{
    public ushort ProductId { get; set; }
    public string ProductIdHex { get; set; } = string.Empty;
    public string BeginHex { get; set; } = string.Empty;
    public DateTimeOffset BeginUtc { get; set; }
    public string EndHex { get; set; } = string.Empty;
    public DateTimeOffset EndUtc { get; set; }
}
