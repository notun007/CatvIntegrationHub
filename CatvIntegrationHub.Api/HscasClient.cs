using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace CatvIntegrationHub.Api;

public sealed partial class HscasClient
{
    private const string CasVersionHex = "02";
    private readonly HscasOptions _options;
    private ushort _sessionId;

    public HscasClient(IOptions<HscasOptions> options)
    {
        _options = options.Value;
    }

    public QueryAuthorizationResponse QueryAuthorizationOfCertainTerminal(string cardId)
    {
        QueryAuthorizationResponse diagnostic = new() { CommandName = "QueryAuthorizationOfCertainTerminal" };
        string phase = "validate";
        QueryAuthorizationResponse Fail(string code, string reason, int httpStatus)
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
                return Fail("QA100", "Card ID is missing.", 400);
            string normalized = cardId.Trim();
            if (normalized.Any(c => c < '0' || c > '9'))
                return Fail("QA101", "Card ID must contain decimal digits only.", 400);
            if (!uint.TryParse(normalized, out uint id) || id == 0 || id == uint.MaxValue)
                return Fail("QA102", "Card ID must be between 1 and 4294967294.", 400);
            string cardIdHex = id.ToString("X8");

            string sessionIdHex = GetNextSessionIdHex();
            string requestHex = BuildCasPacket(sessionIdHex, "20", "0004", cardIdHex);
            diagnostic.RequestHex = requestHex;
            byte[] request = ConvertHexToByteArray(requestHex);

            if (!IPAddress.TryParse(_options.IpAddress, out var address) || _options.Port is < 1 or > 65535
                || _options.SendTimeoutMilliseconds <= 0 || _options.ReceiveTimeoutMilliseconds <= 0)
                return Fail("QA500", "CAS connection configuration is invalid.", 500);
            using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                SendTimeout = _options.SendTimeoutMilliseconds,
                ReceiveTimeout = _options.ReceiveTimeoutMilliseconds
            };
            phase = "connect";
            using (var timeout = new CancellationTokenSource(_options.SendTimeoutMilliseconds))
                socket.ConnectAsync(address, _options.Port, timeout.Token).AsTask().GetAwaiter().GetResult();
            phase = "send";
            SendAll(socket, request);
            phase = "receive";
            CasResponse response = ReadCasResponse(socket);
            diagnostic.ResponseHex = BytesToHex(response.FullResponse);
            diagnostic.BodyHex = BytesToHex(response.Body);
            if (response.SessionId != Convert.ToUInt16(sessionIdHex, 16))
                return Fail("QA401", "CAS response session does not match the request.", 502);
            if (response.CasVersion != 2)
                return Fail("QA403", "CAS response version is unsupported.", 502);
            if (response.CommandType != 0x20)
                return Fail("QA402", "CAS response command does not match the request.", 502);

            uint result = ReadResultCode(response.Body);
            QueryAuthorizationResponse apiResponse = new()
            {
                CommandName = "QueryAuthorizationOfCertainTerminal",
                RequestHex = requestHex,
                ResponseHex = BytesToHex(response.FullResponse),
                BodyHex = BytesToHex(response.Body),
                ResultHex = result.ToString("X8"),
                ResultDecimal = result,
                ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result),
                Success = result == 0
            };

            if (response.Body.Length >= 6)
                apiResponse.ReturnAmount = ReadUInt16BE(response.Body, 4);

            diagnostic = apiResponse;
            if (result != 0)
            {
                string code = result switch
                {
                    0xD0000002 => "QA201", 0xD0000004 => "QA202", 0xD0000005 => "QA203",
                    0xD000000A => "QA204", 0xD000001E => "QA205", 0xFFFFFFFF => "QA206",
                    0xD000001B => "QA207", 0xD000000B => "QA208", 0xD000001F => "QA209",
                    0xD0000020 => "QA210", 0xD0000022 => "QA211", _ => "QA299"
                };
                return Fail(code, apiResponse.ResultMeaning, 200);
            }

            if (response.Body.Length < 6)
                return Fail("QA404", "Successful query response is missing the authorization count.", 502);

            ushort returnAmount = ReadUInt16BE(response.Body, 4);
            int expectedBodyLength = 6 + (returnAmount * 10);
            if (response.Body.Length != expectedBodyLength)
                return Fail("QA404", "Authorization count does not match the response length.", 502);

            int offset = 6;
            for (int i = 0; i < returnAmount; i++)
            {
                ushort productId = ReadUInt16BE(response.Body, offset);
                uint beginUnix = ReadUInt32BE(response.Body, offset + 2);
                uint endUnix = ReadUInt32BE(response.Body, offset + 6);

                apiResponse.Authorizations.Add(new AuthorizationRecord
                {
                    ProductId = productId,
                    ProductIdHex = productId.ToString("X4"),
                    BeginHex = beginUnix.ToString("X8"),
                    BeginUtc = DateTimeOffset.FromUnixTimeSeconds(beginUnix),
                    EndHex = endUnix.ToString("X8"),
                    EndUtc = DateTimeOffset.FromUnixTimeSeconds(endUnix)
                });

                offset += 10;
            }

            // HSCAS section 4.3: begin < end is authorization; begin >= end is un-authorization.
            // A successful query can return retained un-authorization records. Keep the raw list intact.
            bool hasAuthorizationPeriods = apiResponse.Authorizations.Any(record => record.BeginUtc < record.EndUtc);
            apiResponse.ReasonCode = hasAuthorizationPeriods ? "QA000" : "QA001";
            apiResponse.Reason = hasAuthorizationPeriods
                ? "Account is open with authorization periods; check their dates for current validity."
                : returnAmount == 0
                    ? "Account is open without authorization records."
                    : "Account is open; only un-authorization records were returned.";
            return apiResponse;

        }
        catch (OperationCanceledException)
        { return Fail("QA301", "CAS connection timed out.", 504); }
        catch (SocketException ex)
        {
            var (code, reason) = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => ("QA300", "CAS connection was refused."),
                SocketError.TimedOut when phase == "connect" => ("QA301", "CAS connection timed out."),
                SocketError.TimedOut when phase == "receive" => ("QA303", "Timed out waiting for CAS response."),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown => ("QA302", "CAS host or network is unreachable."),
                SocketError.ConnectionReset or SocketError.ConnectionAborted => ("QA305", "CAS connection was reset."),
                _ => ("QA306", "CAS transport failed during " + phase + ".")
            };
            return Fail(code, reason, code is "QA301" or "QA303" ? 504 : 502);
        }
        catch (InvalidDataException)
        { return Fail("QA400", "CAS response packet is malformed.", 502); }
        catch (IOException)
        { return Fail("QA304", "CAS connection closed before the exchange completed.", 502); }
    }

    private CasCommandResponse SendCasCommand(string commandName, string requestHex)
    {
        byte[] request = ConvertHexToByteArray(requestHex);

        using Socket socket = CreateConnectedSocket();
        SendAll(socket, request);
        CasResponse response = ReadCasResponse(socket);

        uint result = ReadResultCode(response.Body);
        return new CasCommandResponse
        {
            CommandName = commandName,
            RequestHex = requestHex,
            ResponseHex = BytesToHex(response.FullResponse),
            BodyHex = BytesToHex(response.Body),
            ResultHex = result.ToString("X8"),
            ResultDecimal = result,
            ResultMeaning = result == 0 ? "SUCCESS" : GetCasError(result),
            Success = result == 0
        };
    }

    private static string BuildAuthorizationBody(string cardId, string productIdHex, string beginTimeHex, string endTimeHex)
    {
        return cardId.ToUpperInvariant() +
               "01" +
               "01" +
               "00" +
               productIdHex.ToUpperInvariant() +
               beginTimeHex.ToUpperInvariant() +
               endTimeHex.ToUpperInvariant() +
               "00";
    }

    private string BuildCasPacket(string commandTypeHex, string dataLengthHex, string bodyHex)
    {
        return BuildCasPacket(GetNextSessionIdHex(), commandTypeHex, dataLengthHex, bodyHex);
    }

    private static string BuildCasPacket(string sessionIdHex, string commandTypeHex, string dataLengthHex, string bodyHex)
    {
        return sessionIdHex.ToUpperInvariant() +
               CasVersionHex +
               commandTypeHex.ToUpperInvariant() +
               dataLengthHex.ToUpperInvariant() +
               bodyHex.ToUpperInvariant();
    }

    private string GetNextSessionIdHex()
    {
        if (_sessionId == ushort.MaxValue)
            _sessionId = 1;
        else
            _sessionId++;

        return _sessionId.ToString("X4");
    }

    private Socket CreateConnectedSocket()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            SendTimeout = _options.SendTimeoutMilliseconds,
            ReceiveTimeout = _options.ReceiveTimeoutMilliseconds
        };

        socket.Connect(IPAddress.Parse(_options.IpAddress), _options.Port);
        return socket;
    }

    private static int SendAll(Socket socket, byte[] message)
    {
        int totalSent = 0;
        while (totalSent < message.Length)
        {
            int sent = socket.Send(message, totalSent, message.Length - totalSent, SocketFlags.None);
            if (sent <= 0)
                throw new IOException("CAS connection closed while sending request.");
            totalSent += sent;
        }

        return totalSent;
    }

    private static CasResponse ReadCasResponse(Socket socket)
    {
        byte[] header = ReceiveExactly(socket, 6);
        ushort dataLength = ReadUInt16BE(header, 4);
        byte[] body = dataLength > 0 ? ReceiveExactly(socket, dataLength) : Array.Empty<byte>();
        byte[] fullResponse = new byte[header.Length + body.Length];

        Buffer.BlockCopy(header, 0, fullResponse, 0, header.Length);
        if (body.Length > 0)
            Buffer.BlockCopy(body, 0, fullResponse, header.Length, body.Length);

        return new CasResponse
        {
            Header = header,
            Body = body,
            FullResponse = fullResponse,
            SessionId = ReadUInt16BE(header, 0),
            CasVersion = header[2],
            CommandType = header[3],
            DataLength = dataLength
        };
    }

    private static byte[] ReceiveExactly(Socket socket, int length)
    {
        byte[] buffer = new byte[length];
        int totalReceived = 0;

        while (totalReceived < length)
        {
            int received = socket.Receive(buffer, totalReceived, length - totalReceived, SocketFlags.None);
            if (received == 0)
                throw new IOException("CAS server closed the connection before the complete response was received.");
            totalReceived += received;
        }

        return buffer;
    }

    private static void ValidateResponseHeader(CasResponse response, ushort expectedSession, byte expectedCommand)
    {
        if (response.SessionId != expectedSession)
            throw new InvalidDataException("CAS response Session ID does not match the request.");
        if (response.CasVersion != 0x02)
            throw new InvalidDataException("CAS response Version does not match the request.");
        if (response.CommandType != expectedCommand)
            throw new InvalidDataException("CAS response Command Type does not match the request.");
    }

    private static uint ReadResultCode(byte[] body)
    {
        if (body.Length < 4)
            throw new InvalidDataException("CAS response body is shorter than the 4-byte Result field.");

        return ReadUInt32BE(body, 0);
    }

    private static byte[] ConvertHexToByteArray(string hexString)
    {
        if (string.IsNullOrWhiteSpace(hexString))
            throw new ArgumentException("HEX string cannot be null or empty.", nameof(hexString));

        hexString = hexString.Trim();
        if ((hexString.Length & 1) != 0)
            throw new FormatException("HEX string must contain an even number of characters.");

        byte[] byteArray = new byte[hexString.Length / 2];
        for (int i = 0; i < hexString.Length; i += 2)
            byteArray[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);

        return byteArray;
    }

    private static void ValidateHex(string value, int expectedLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != expectedLength)
            throw new ArgumentException($"{parameterName} must contain exactly {expectedLength} hexadecimal characters.", parameterName);

        Convert.ToUInt64(value, 16);
    }

    private static string ConvertCardIdToHex(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            throw new ArgumentException("cardId must be a decimal string.", nameof(cardId));

        string normalizedCardId = cardId.Trim();
        if (!uint.TryParse(normalizedCardId, out uint parsedCardId))
            throw new ArgumentException("cardId must contain only decimal digits and fit within an unsigned 32-bit integer.", nameof(cardId));

        if (parsedCardId == 0)
            throw new ArgumentException("cardId must be greater than 0.", nameof(cardId));

        if (parsedCardId == uint.MaxValue)
            throw new ArgumentException("cardId must be less than 4294967295.", nameof(cardId));

        return parsedCardId.ToString("X8");
    }

    private static string ConvertProductIdToHex(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("productId must be a decimal or hexadecimal string.", nameof(productId));

        string normalizedProductId = productId.Trim();
        ushort parsedProductId;

        if (normalizedProductId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ushort.TryParse(normalizedProductId[2..], System.Globalization.NumberStyles.HexNumber, null, out parsedProductId))
                throw new ArgumentException("productId must fit within a 2-byte unsigned value.", nameof(productId));
        }
        else if (normalizedProductId.Any(c => char.IsLetter(c)))
        {
            if (!ushort.TryParse(normalizedProductId, System.Globalization.NumberStyles.HexNumber, null, out parsedProductId))
                throw new ArgumentException("productId must be a decimal or hexadecimal string.", nameof(productId));
        }
        else if (!ushort.TryParse(normalizedProductId, out parsedProductId))
        {
            throw new ArgumentException("productId must fit within a 2-byte unsigned value.", nameof(productId));
        }

        if (parsedProductId == 0)
            throw new ArgumentException("productId must be greater than 0.", nameof(productId));

        return parsedProductId.ToString("X4");
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static string BytesToHex(byte[] data)
    {
        return BitConverter.ToString(data).Replace("-", "");
    }

    private static string GetCasError(uint result)
    {
        return result switch
        {
            0xD0000002 => "CAS version incorrect",
            0xD0000004 => "Unsupported command",
            0xD0000005 => "Data packet length incorrect",
            0xD000000A => "Terminal ID cannot be 0",
            0xD000000B => "Expired time is incorrect",
            0xD000000F => "Terminal status must be 0 (inactivate) or 1 (activate)",
            0xD000001B => "Terminal ID cannot be FFFFFFFF",
            0xD000001E => "Terminal account is not opened",
            0xD000001F => "Chip ID length must be 8",
            0xD0000020 => "Chip ID count must be 1",
            0xD0000022 => "Product ID cannot be 0",
            0xFFFFFFFF => "HSCAS returned FFFFFFFF (generic API communication error)",
            _ => $"Unknown CAS error 0x{result:X8}"
        };
    }

    private sealed class CasResponse
    {
        public byte[] Header { get; init; } = Array.Empty<byte>();
        public byte[] Body { get; init; } = Array.Empty<byte>();
        public byte[] FullResponse { get; init; } = Array.Empty<byte>();
        public ushort SessionId { get; init; }
        public byte CasVersion { get; init; }
        public byte CommandType { get; init; }
        public ushort DataLength { get; init; }
    }
}
