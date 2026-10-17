using System.Net;
using System.Net.Sockets;

namespace CatvIntegrationHub.Api;

public sealed partial class HscasClient
{
    public CloseAccountResponse CloseAccount(string cardId)
    {
        CloseAccountResponse diagnostic = new() { CommandName = "CloseAccount" };
        string phase = "validate";
        CloseAccountResponse Fail(string code, string reason, int httpStatus)
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
                return Fail("CA100", "Card ID is missing.", 400);
            string normalized = cardId.Trim();
            if (normalized.Any(c => c < '0' || c > '9'))
                return Fail("CA101", "Card ID must contain decimal digits only.", 400);
            if (!uint.TryParse(normalized, out uint id) || id == 0 || id == uint.MaxValue)
                return Fail("CA102", "Card ID must be between 1 and 4294967294.", 400);
            string sessionIdHex = GetNextSessionIdHex();
            string bodyHex = id.ToString("X8") + "01" +
                DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds().ToString("X8");
            string requestHex = BuildCasPacket(sessionIdHex, "15", "0009", bodyHex);
            diagnostic.RequestHex = requestHex;

            if (!IPAddress.TryParse(_options.IpAddress, out var address) || _options.Port is < 1 or > 65535
                || _options.SendTimeoutMilliseconds <= 0 || _options.ReceiveTimeoutMilliseconds <= 0)
                return Fail("CA500", "CAS connection configuration is invalid.", 500);

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
                return Fail("CA401", "CAS response session does not match the request.", 502);
            if (response.CasVersion != 2)
                return Fail("CA403", "CAS response version is unsupported.", 502);
            if (response.CommandType != 0x15)
                return Fail("CA402", "CAS response command does not match the request.", 502);
            uint result = ReadResultCode(response.Body);
            diagnostic.ResultHex = result.ToString("X8");
            diagnostic.ResultDecimal = result;
            diagnostic.ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result);
            if (result != 0)
            {
                string code = result switch
                {
                    0xD0000002 => "CA201", 0xD0000004 => "CA202", 0xD0000005 => "CA203",
                    0xD000000A => "CA204", 0xD000001E => "CA205", 0xFFFFFFFF => "CA206",
                    0xD000001B => "CA207", 0xD000000B => "CA208",
                    _ => "CA299"
                };
                return Fail(code, diagnostic.ResultMeaning, 200);
            }

            // HSCAS section 4.2.2: Result, Emm_Count, then Emm_Len/Emm_Data per packet.
            if (response.Body.Length < 5)
                return Fail("CA404", "Successful close account response is missing the EMM count.", 502);
            int offset = 5;
            int emmCount = response.Body[4];
            for (int i = 0; i < emmCount; i++)
            {
                if (offset >= response.Body.Length)
                    return Fail("CA404", "EMM count does not match the close account response length.", 502);
                int emmLength = response.Body[offset++];
                if (emmLength > response.Body.Length - offset)
                    return Fail("CA404", "EMM data is shorter than its declared length.", 502);
                offset += emmLength;
            }
            if (offset != response.Body.Length)
                return Fail("CA404", "Close account response contains unexpected trailing EMM data.", 502);

            diagnostic.Success = true;
            diagnostic.ReasonCode = "CA000";
            diagnostic.Reason = "Account closed successfully.";
            return diagnostic;
        }
        catch (OperationCanceledException)
        { return Fail("CA301", "CAS connection timed out.", 504); }
        catch (SocketException ex)
        {
            var (code, reason) = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => ("CA300", "CAS connection was refused."),
                SocketError.TimedOut when phase == "connect" => ("CA301", "CAS connection timed out."),
                SocketError.TimedOut when phase == "receive" => ("CA303", "Timed out waiting for CAS response."),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown => ("CA302", "CAS host or network is unreachable."),
                SocketError.ConnectionReset or SocketError.ConnectionAborted => ("CA305", "CAS connection was reset."),
                _ => ("CA306", "CAS transport failed during " + phase + ".")
            };
            return Fail(code, reason, code is "CA301" or "CA303" ? 504 : 502);
        }
        catch (InvalidDataException)
        { return Fail("CA400", "CAS response packet is malformed.", 502); }
        catch (IOException)
        { return Fail("CA304", "CAS connection closed before the exchange completed.", 502); }
    }
}
