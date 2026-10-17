using System.Net;
using System.Net.Sockets;

namespace CatvIntegrationHub.Api;

public sealed partial class HscasClient
{
    public ClearAuthorizationResponse ClearAuthorizationOfTerminal(string cardId)
    {
        ClearAuthorizationResponse diagnostic = new() { CommandName = "ClearAuthorizationOfTerminal" };
        string phase = "validate";
        ClearAuthorizationResponse Fail(string code, string reason, int httpStatus)
        {
            diagnostic.Success = false;
            diagnostic.ReasonCode = code;
            diagnostic.Reason = reason;
            diagnostic.HttpStatusCode = httpStatus;
            return diagnostic;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return Fail("CL100", "Card ID is missing.", 400);
            string normalized = cardId.Trim();
            if (normalized.Any(c => c < '0' || c > '9'))
                return Fail("CL101", "Card ID must contain decimal digits only.", 400);
            if (!uint.TryParse(normalized, out uint id) || id == 0 || id == uint.MaxValue)
                return Fail("CL102", "Card ID must be between 1 and 4294967294.", 400);
            string sessionIdHex = GetNextSessionIdHex();
            string bodyHex = id.ToString("X8") + "01";
            string requestHex = BuildCasPacket(sessionIdHex, "1F", "0005", bodyHex);
            diagnostic.RequestHex = requestHex;

            if (!IPAddress.TryParse(_options.IpAddress, out var address) || _options.Port is < 1 or > 65535
                || _options.SendTimeoutMilliseconds <= 0 || _options.ReceiveTimeoutMilliseconds <= 0)
                return Fail("CL500", "CAS connection configuration is invalid.", 500);

            using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                SendTimeout = _options.SendTimeoutMilliseconds,
                ReceiveTimeout = _options.ReceiveTimeoutMilliseconds
            };
            phase = "connect";
            using (var timeout = new CancellationTokenSource(_options.SendTimeoutMilliseconds))
                socket.ConnectAsync(address, _options.Port, timeout.Token).AsTask().GetAwaiter().GetResult();
            phase = "send";
            SendAll(socket, ConvertHexToByteArray(requestHex));
            phase = "receive";
            CasResponse response = ReadCasResponse(socket);
            diagnostic.ResponseHex = BytesToHex(response.FullResponse);
            diagnostic.BodyHex = BytesToHex(response.Body);
            if (response.SessionId != Convert.ToUInt16(sessionIdHex, 16))
                return Fail("CL401", "CAS response session does not match the request.", 502);
            if (response.CasVersion != 2)
                return Fail("CL403", "CAS response version is unsupported.", 502);
            if (response.CommandType != 0x1F)
                return Fail("CL402", "CAS response command does not match the request.", 502);
            // HSCAS section 4.4.2 defines only the four-byte Result field.
            if (response.Body.Length != 4)
                return Fail("CL400", "Clear authorization response must contain exactly a 4-byte result.", 502);

            uint result = ReadResultCode(response.Body);
            diagnostic.ResultHex = result.ToString("X8");
            diagnostic.ResultDecimal = result;
            diagnostic.ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result);
            if (result != 0)
            {
                string code = result switch
                {
                    0xD0000002 => "CL201", 0xD0000004 => "CL202", 0xD0000005 => "CL203",
                    0xD000000A => "CL204", 0xD000001E => "CL205", 0xFFFFFFFF => "CL206",
                    0xD000001B => "CL207", _ => "CL299"
                };
                return Fail(code, diagnostic.ResultMeaning, 200);
            }

            diagnostic.Success = true;
            diagnostic.ReasonCode = "CL000";
            diagnostic.Reason = "All authorizations cleared successfully.";
            return diagnostic;
        }
        catch (OperationCanceledException)
        { return Fail("CL301", "CAS connection timed out.", 504); }
        catch (SocketException ex)
        {
            var (code, reason) = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => ("CL300", "CAS connection was refused."),
                SocketError.TimedOut when phase == "connect" => ("CL301", "CAS connection timed out."),
                SocketError.TimedOut when phase == "receive" => ("CL303", "Timed out waiting for CAS response."),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown => ("CL302", "CAS host or network is unreachable."),
                SocketError.ConnectionReset or SocketError.ConnectionAborted => ("CL305", "CAS connection was reset."),
                _ => ("CL306", "CAS transport failed during " + phase + ".")
            };
            return Fail(code, reason, code is "CL301" or "CL303" ? 504 : 502);
        }
        catch (InvalidDataException)
        { return Fail("CL400", "CAS response packet is malformed.", 502); }
        catch (IOException)
        { return Fail("CL304", "CAS connection closed before the exchange completed.", 502); }
    }
}
