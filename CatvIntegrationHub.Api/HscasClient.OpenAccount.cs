using System.Net;
using System.Net.Sockets;

namespace CatvIntegrationHub.Api;

public sealed partial class HscasClient
{
    public OpenAccountResponse OpenAccount(string cardId, string chipId)
    {
        OpenAccountResponse diagnostic = new() { CommandName = "OpenAccount" };
        string phase = "validate";
        OpenAccountResponse Fail(string code, string reason, int httpStatus)
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
                return Fail("OA100", "Card ID is missing.", 400);
            string normalized = cardId.Trim();
            if (normalized.Any(c => c < '0' || c > '9'))
                return Fail("OA101", "Card ID must contain decimal digits only.", 400);
            if (!uint.TryParse(normalized, out uint id) || id == 0 || id == uint.MaxValue)
                return Fail("OA102", "Card ID must be between 1 and 4294967294.", 400);
            if (string.IsNullOrWhiteSpace(chipId))
                return Fail("OA104", "Chip ID is missing.", 400);
            if (chipId.Length != 16 || chipId.Any(c => !Uri.IsHexDigit(c)))
                return Fail("OA105", "Chip ID must contain exactly 16 hexadecimal characters (8 bytes).", 400);

            string sessionIdHex = GetNextSessionIdHex();
            string bodyHex = id.ToString("X8") +
                DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds().ToString("X8") +
                "0108" + chipId.ToUpperInvariant();
            string requestHex = BuildCasPacket(sessionIdHex, "1B", "0012", bodyHex);
            diagnostic.RequestHex = requestHex;

            if (!IPAddress.TryParse(_options.IpAddress, out var address) || _options.Port is < 1 or > 65535
                || _options.SendTimeoutMilliseconds <= 0 || _options.ReceiveTimeoutMilliseconds <= 0)
                return Fail("OA500", "CAS connection configuration is invalid.", 500);

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
                return Fail("OA401", "CAS response session does not match the request.", 502);
            if (response.CasVersion != 2)
                return Fail("OA403", "CAS response version is unsupported.", 502);
            if (response.CommandType != 0x1B)
                return Fail("OA402", "CAS response command does not match the request.", 502);
            // HSCAS section 4.1.2 defines only the four-byte Result field.
            if (response.Body.Length != 4)
                return Fail("OA400", "Open account response must contain exactly a 4-byte result.", 502);

            uint result = ReadResultCode(response.Body);
            diagnostic.ResultHex = result.ToString("X8");
            diagnostic.ResultDecimal = result;
            diagnostic.ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result);
            if (result != 0)
            {
                string code = result switch
                {
                    0xD0000002 => "OA201", 0xD0000004 => "OA202", 0xD0000005 => "OA203",
                    0xD000000A => "OA204", 0xD000001E => "OA205", 0xFFFFFFFF => "OA206",
                    0xD000001B => "OA207", 0xD000000B => "OA208", 0xD000001F => "OA209",
                    0xD0000020 => "OA210", _ => "OA299"
                };
                return Fail(code, diagnostic.ResultMeaning, 200);
            }

            diagnostic.Success = true;
            diagnostic.ReasonCode = "OA000";
            diagnostic.Reason = "Account opened successfully.";
            return diagnostic;
        }
        catch (OperationCanceledException)
        { return Fail("OA301", "CAS connection timed out.", 504); }
        catch (SocketException ex)
        {
            var (code, reason) = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => ("OA300", "CAS connection was refused."),
                SocketError.TimedOut when phase == "connect" => ("OA301", "CAS connection timed out."),
                SocketError.TimedOut when phase == "receive" => ("OA303", "Timed out waiting for CAS response."),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown => ("OA302", "CAS host or network is unreachable."),
                SocketError.ConnectionReset or SocketError.ConnectionAborted => ("OA305", "CAS connection was reset."),
                _ => ("OA306", "CAS transport failed during " + phase + ".")
            };
            return Fail(code, reason, code is "OA301" or "OA303" ? 504 : 502);
        }
        catch (InvalidDataException)
        { return Fail("OA400", "CAS response packet is malformed.", 502); }
        catch (IOException)
        { return Fail("OA304", "CAS connection closed before the exchange completed.", 502); }
    }
}
