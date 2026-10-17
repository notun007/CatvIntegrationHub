using System.Net;
using System.Net.Sockets;

namespace CatvIntegrationHub.Api;

public sealed partial class HscasClient
{
    public SetTerminalStatusResponse SetTerminalStatus(string cardId, int status)
    {
        SetTerminalStatusResponse diagnostic = new() { CommandName = "SetTerminalStatus" };
        string phase = "validate";
        SetTerminalStatusResponse Fail(string code, string reason, int httpStatus)
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
                return Fail("TS100", "Card ID is missing.", 400);
            string normalized = cardId.Trim();
            if (normalized.Any(c => c < '0' || c > '9'))
                return Fail("TS101", "Card ID must contain decimal digits only.", 400);
            if (!uint.TryParse(normalized, out uint id) || id == 0 || id == uint.MaxValue)
                return Fail("TS102", "Card ID must be between 1 and 4294967294.", 400);
            if (status is not (0 or 1))
                return Fail("TS104", "status must be 0 (inactivate) or 1 (activate).", 400);
            string sessionIdHex = GetNextSessionIdHex();
            // HSCAS section 4.9 recommends send time + 3 days for both status values.
            string bodyHex = id.ToString("X8") + "01" + status.ToString("X2") +
                DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds().ToString("X8");
            string requestHex = BuildCasPacket(sessionIdHex, "0B", "000A", bodyHex);
            diagnostic.RequestHex = requestHex;

            if (!IPAddress.TryParse(_options.IpAddress, out var address) || _options.Port is < 1 or > 65535
                || _options.SendTimeoutMilliseconds <= 0 || _options.ReceiveTimeoutMilliseconds <= 0)
                return Fail("TS500", "CAS connection configuration is invalid.", 500);

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
                return Fail("TS401", "CAS response session does not match the request.", 502);
            if (response.CasVersion != 2)
                return Fail("TS403", "CAS response version is unsupported.", 502);
            if (response.CommandType != 0x0B)
                return Fail("TS402", "CAS response command does not match the request.", 502);
            uint result = ReadResultCode(response.Body);
            diagnostic.ResultHex = result.ToString("X8");
            diagnostic.ResultDecimal = result;
            diagnostic.ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result);
            if (result != 0)
            {
                string code = result switch
                {
                    0xD0000002 => "TS201", 0xD0000004 => "TS202", 0xD0000005 => "TS203",
                    0xD000000A => "TS204", 0xD000001E => "TS205", 0xFFFFFFFF => "TS206",
                    0xD000001B => "TS207", 0xD000000B => "TS208",
                    0xD000000F => "TS209",
                    _ => "TS299"
                };
                return Fail(code, diagnostic.ResultMeaning, 200);
            }

            // HSCAS section 4.9.2: Result, Emm_Count, then Emm_Len/Emm_Data per packet.
            if (response.Body.Length < 5)
                return Fail("TS404", "Successful set terminal status response is missing the EMM count.", 502);
            int offset = 5;
            int emmCount = response.Body[4];
            for (int i = 0; i < emmCount; i++)
            {
                if (offset >= response.Body.Length)
                    return Fail("TS404", "EMM count does not match the set terminal status response length.", 502);
                int emmLength = response.Body[offset++];
                if (emmLength > response.Body.Length - offset)
                    return Fail("TS404", "EMM data is shorter than its declared length.", 502);
                offset += emmLength;
            }
            if (offset != response.Body.Length)
                return Fail("TS404", "Set terminal status response contains unexpected trailing EMM data.", 502);

            diagnostic.Success = true;
            diagnostic.ReasonCode = "TS000";
            diagnostic.Reason = status == 1 ? "Terminal activated successfully." : "Terminal inactivated successfully.";
            return diagnostic;
        }
        catch (OperationCanceledException)
        { return Fail("TS301", "CAS connection timed out.", 504); }
        catch (SocketException ex)
        {
            var (code, reason) = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => ("TS300", "CAS connection was refused."),
                SocketError.TimedOut when phase == "connect" => ("TS301", "CAS connection timed out."),
                SocketError.TimedOut when phase == "receive" => ("TS303", "Timed out waiting for CAS response."),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown => ("TS302", "CAS host or network is unreachable."),
                SocketError.ConnectionReset or SocketError.ConnectionAborted => ("TS305", "CAS connection was reset."),
                _ => ("TS306", "CAS transport failed during " + phase + ".")
            };
            return Fail(code, reason, code is "TS301" or "TS303" ? 504 : 502);
        }
        catch (InvalidDataException)
        { return Fail("TS400", "CAS response packet is malformed.", 502); }
        catch (IOException)
        { return Fail("TS304", "CAS connection closed before the exchange completed.", 502); }
    }
}
