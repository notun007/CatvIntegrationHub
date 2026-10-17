using System.Net;
using System.Net.Sockets;

namespace CatvIntegrationHub.Api;

public sealed partial class HscasClient
{
    public AuthorizationCommandResponse Authorization(string cardId, string productId, DateTimeOffset startTime, DateTimeOffset endTime) =>
        ExecuteAuthorization(cardId, productId, startTime, endTime, false);

    public AuthorizationCommandResponse UnAuthorizationDeleteAuthorization(string cardId, string productId, DateTimeOffset startTime, DateTimeOffset endTime) =>
        ExecuteAuthorization(cardId, productId, startTime, endTime, true);

    private AuthorizationCommandResponse ExecuteAuthorization(string cardId, string productId,
        DateTimeOffset startTime, DateTimeOffset endTime, bool unauthorize)
    {
        string prefix = unauthorize ? "UA" : "AU";
        AuthorizationCommandResponse diagnostic = new()
        {
            CommandName = unauthorize ? "UnAuthorizationDeleteAuthorization" : "Authorization",
            ReasonCode = prefix + "599"
        };
        string phase = "validate";
        AuthorizationCommandResponse Fail(string code, string reason, int httpStatus)
        {
            diagnostic.Success = false;
            diagnostic.ReasonCode = prefix + code;
            diagnostic.Reason = reason;
            diagnostic.HttpStatusCode = httpStatus;
            return diagnostic;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return Fail("100", "Card ID is missing.", 400);
            string normalized = cardId.Trim();
            if (normalized.Any(c => c < '0' || c > '9'))
                return Fail("101", "Card ID must contain decimal digits only.", 400);
            if (!uint.TryParse(normalized, out uint id) || id == 0 || id == uint.MaxValue)
                return Fail("102", "Card ID must be between 1 and 4294967294.", 400);
            if (string.IsNullOrWhiteSpace(productId))
                return Fail("104", "Product ID is missing.", 400);
            string productIdHex;
            try { productIdHex = ConvertProductIdToHex(productId); }
            catch (ArgumentException ex) { return Fail("105", ex.Message, 400); }
            if (endTime <= startTime)
                return Fail("106", "endTime must be later than startTime.", 400);
            if (endTime <= DateTimeOffset.UtcNow)
                return Fail("107", "endTime must be later than the current time.", 400);
            long beginUnix = startTime.ToUnixTimeSeconds();
            long endUnix = endTime.ToUnixTimeSeconds();
            if (beginUnix < 0 || beginUnix > uint.MaxValue || endUnix > uint.MaxValue)
                return Fail("108", "Start and end times must fit in unsigned 32-bit Unix seconds.", 400);

            string sessionIdHex = GetNextSessionIdHex();
            // Preserve existing un-authorization encoding: begin = end = requested endTime.
            string bodyHex = BuildAuthorizationBody(id.ToString("X8"), productIdHex,
                (unauthorize ? endUnix : beginUnix).ToString("X8"), endUnix.ToString("X8"));
            string requestHex = BuildCasPacket(sessionIdHex, "01", "0012", bodyHex);
            diagnostic.RequestHex = requestHex;

            if (!IPAddress.TryParse(_options.IpAddress, out var address) || _options.Port is < 1 or > 65535
                || _options.SendTimeoutMilliseconds <= 0 || _options.ReceiveTimeoutMilliseconds <= 0)
                return Fail("500", "CAS connection configuration is invalid.", 500);

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
                return Fail("401", "CAS response session does not match the request.", 502);
            if (response.CasVersion != 2)
                return Fail("403", "CAS response version is unsupported.", 502);
            if (response.CommandType != 0x01)
                return Fail("402", "CAS response command does not match the request.", 502);
            uint result = ReadResultCode(response.Body);
            diagnostic.ResultHex = result.ToString("X8");
            diagnostic.ResultDecimal = result;
            diagnostic.ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result);
            if (result != 0)
            {
                string code = result switch
                {
                    0xD0000002 => "201", 0xD0000004 => "202", 0xD0000005 => "203",
                    0xD000000A => "204", 0xD000001E => "205", 0xFFFFFFFF => "206",
                    0xD000001B => "207", 0xD0000022 => "211",
                    _ => "299"
                };
                return Fail(code, diagnostic.ResultMeaning, 200);
            }

            // HSCAS section 4.3.2: Result, ReturnAmount, then ProductNumber/Desc_Len/Description.
            if (response.Body.Length < 5)
                return Fail("404", "Successful authorization response is missing the returned-product count.", 502);
            int offset = 5;
            int returnAmount = response.Body[4];
            for (int i = 0; i < returnAmount; i++)
            {
                if (response.Body.Length - offset < 3)
                    return Fail("404", "Returned-product count does not match the response length.", 502);
                int descriptionLength = response.Body[offset + 2];
                offset += 3;
                if (descriptionLength > response.Body.Length - offset)
                    return Fail("404", "Returned product description is shorter than its declared length.", 502);
                offset += descriptionLength;
            }
            if (offset != response.Body.Length)
                return Fail("404", "Authorization response contains unexpected trailing product data.", 502);

            diagnostic.Success = true;
            diagnostic.ReasonCode = prefix + "000";
            diagnostic.Reason = unauthorize ? "Product unauthorized successfully." : "Product authorized successfully.";
            return diagnostic;
        }
        catch (OperationCanceledException)
        { return Fail("301", "CAS connection timed out.", 504); }
        catch (SocketException ex)
        {
            var (code, reason) = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => ("300", "CAS connection was refused."),
                SocketError.TimedOut when phase == "connect" => ("301", "CAS connection timed out."),
                SocketError.TimedOut when phase == "receive" => ("303", "Timed out waiting for CAS response."),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown => ("302", "CAS host or network is unreachable."),
                SocketError.ConnectionReset or SocketError.ConnectionAborted => ("305", "CAS connection was reset."),
                _ => ("306", "CAS transport failed during " + phase + ".")
            };
            return Fail(code, reason, code is "301" or "303" ? 504 : 502);
        }
        catch (InvalidDataException)
        { return Fail("400", "CAS response packet is malformed.", 502); }
        catch (IOException)
        { return Fail("304", "CAS connection closed before the exchange completed.", 502); }
    }
}
