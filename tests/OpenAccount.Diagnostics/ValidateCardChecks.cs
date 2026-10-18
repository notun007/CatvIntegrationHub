using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatvIntegrationHub.Api;
using CatvIntegrationHub.Application.Cards;
using CatvIntegrationHub.Domain.Enums;

internal static class ValidateCardChecks
{
    public static async Task<int> RunAsync(HttpClient http, HscasOptions options, RecordingStub recorder)
    {
        int checks = 0;
        recorder.ThrowOnSave = false;
        options.ReceiveTimeoutMilliseconds = 300;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        void CheckResponseFields(JsonElement body)
        {
            string[] expectedFields = { "success", "status", "cardId", "chipId", "source", "reasonCode", "reason" };
            Check(body.EnumerateObject().Select(property => property.Name).OrderBy(name => name)
                .SequenceEqual(expectedFields.OrderBy(name => name)), "ValidateCard must return exactly the seven requested fields: " + body);
        }
        async Task<JsonElement> Scenario(uint queryResult, uint? openResult, uint? closeResult,
            bool expected, string source, string cardId = "2147483649", string chipId = "00000A00010A0C00",
            bool closeDisconnect = false, bool saveFailure = false)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var operations = new List<(byte Command, uint Result)> { (0x20, queryResult) };
            if (openResult.HasValue) operations.Add((0x1B, openResult.Value));
            if (closeResult.HasValue) operations.Add((0x15, closeResult.Value));
            int before = recorder.Records.Count;
            recorder.ThrowOnSave = saveFailure;
            var server = Task.Run(async () =>
            {
                foreach (var operation in operations)
                {
                    using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                    using var stream = peer.GetStream();
                    async Task<byte[]> Read(int length)
                    {
                        byte[] data = new byte[length];
                        for (int read = 0; read < length;)
                        {
                            int count = await stream.ReadAsync(data.AsMemory(read), timeout.Token);
                            if (count == 0) throw new Exception("Incomplete CAS request.");
                            read += count;
                        }
                        return data;
                    }
                    byte[] header = await Read(6);
                    byte[] payload = await Read(BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)));
                    Check(header[3] == operation.Command, "ValidateCard command order is incorrect.");
                    Check(BinaryPrimitives.ReadUInt32BigEndian(payload) == uint.Parse(cardId), "Incorrect card ID.");
                    if (operation.Command == 0x1B)
                        Check(Convert.ToHexString(payload.AsSpan(10)) == chipId, "Supplied ChipId was not used.");
                    if (operation.Command == 0x15 && closeDisconnect) continue;
                    int bodyLength = operation.Result != 0 || operation.Command == 0x1B ? 4 : operation.Command == 0x20 ? 6 : 5;
                    byte[] packet = new byte[6 + bodyLength];
                    header.CopyTo(packet, 0);
                    BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), (ushort)bodyLength);
                    BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(6), operation.Result);
                    await stream.WriteAsync(packet, timeout.Token);
                }
            });
            try
            {
                using var response = await http.PostAsJsonAsync("/ValidateCard", new { cardId, chipId }, timeout.Token);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
                await server;
                Check(body.GetProperty("success").GetBoolean() == expected, "Wrong ValidateCard success: " + body);
                Check(body.GetProperty("source").GetString() == source, "Wrong source response.");
                CheckResponseFields(body);
                Check(body.GetProperty("cardId").GetString() == cardId && body.GetProperty("chipId").GetString() == chipId, "Response must preserve card and chip IDs.");
                int expectedRecords = saveFailure ? 0 : operations.Count;
                Check(recorder.Records.Count == before + expectedRecords, "Wrong number of SvCards inserts.");
                if (!saveFailure)
                {
                    var records = recorder.Records.Skip(before).ToArray();
                    Check(records[0].Status == SvCardStatusEnum.QueryAuthorization, "Query audit missing.");
                    if (records.Length > 1) Check(records[1].Status == SvCardStatusEnum.OpenAccount, "Open audit missing.");
                    if (records.Length > 2) Check(records[2].Status == SvCardStatusEnum.CloseAccount, "Close audit missing.");
                    foreach (var record in records)
                        Check(JsonDocument.Parse(record.Json!).RootElement.TryGetProperty("success", out _), "Audit response JSON missing.");
                }
                Check(!listener.Pending(), "An unexpected extra CAS command ran.");
                return body;
            }
            finally { listener.Stop(); recorder.ThrowOnSave = false; }
        }
        var open = await Scenario(0, null, null, true, "QueryAuthorization");
        Check(open.GetProperty("success").GetBoolean(), "Open without authorizations is valid.");
        await Scenario(0xD0000004, null, null, false, "QueryAuthorization");
        await Scenario(0xD000001E, 0xFFFFFFFF, null, false, "OpenAccount");
        await Scenario(0xD000001E, 0, 0, true, "OpenAccount");
        var closeFailed = await Scenario(0xD000001E, 0, 0xFFFFFFFF, true, "OpenAccount");
        Check(closeFailed.GetProperty("reasonCode").GetString() == "OA000" && closeFailed.GetProperty("reason").GetString() == "Account opened successfully.", "Close must not replace the open result.");
        await Scenario(0xD000001E, 0, 0, true, "OpenAccount", closeDisconnect: true);
        await Scenario(0xD000001E, 0, 0, true, "OpenAccount", chipId: "1234567890ABCDEF");
        await Scenario(0, null, null, true, "QueryAuthorization", saveFailure: true);
        await Scenario(0xD000001E, 0, 0, true, "OpenAccount", saveFailure: true);
        // Invalid ChipId must fail before QueryAuthorization, including for an open card.
        foreach (string invalidChip in new[] { "", " ", "invalid", "00000A00010A0C0", "00000A00010A0C000", "00000G00010A0C00" })
        {
            int before = recorder.Count;
            using var response = await http.PostAsJsonAsync("/ValidateCard", new { cardId = "2147483649", chipId = invalidChip });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            CheckResponseFields(body);
            Check((int)response.StatusCode == 400, "Invalid ChipId must return 400 before CAS.");
            Check(!body.GetProperty("success").GetBoolean(), "Invalid ChipId must fail validation.");
            Check(recorder.Count == before, "Invalid ChipId must not execute or audit CAS.");
        }
        foreach (string invalid in new[] { "{\"cardId\":\"2147483649\"}", "{\"cardId\":\"2147483649\",\"chipId\":null}", "{}", "null", "{", "{\"cardId\":\"abc\"}", "{\"cardId\":\"0\"}" })
        {
            int before = recorder.Count;
            using var response = await http.PostAsync("/ValidateCard", new StringContent(invalid, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            CheckResponseFields(body);
            Check((int)response.StatusCode == 400, "Invalid input must return 400.");
            Check(!body.GetProperty("success").GetBoolean(), "Invalid input must fail.");
            Check(recorder.Count == before, "Invalid card input must not create audit rows.");
        }
        options.IpAddress = "invalid";
        using (var response = await http.PostAsJsonAsync("/ValidateCard", new { cardId = "2147483649", chipId = "00000A00010A0C00" }))
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            CheckResponseFields(body);
            Check((int)response.StatusCode == 500 && !body.GetProperty("success").GetBoolean(), "Query configuration failure must stop validation.");
            Check(recorder.LastStatus == SvCardStatusEnum.QueryAuthorization, "Failed query attempt must be audited.");
        }
        options.IpAddress = "127.0.0.1";
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/ValidateCard"))
        {
            request.Headers.Add("X-Test-Unexpected", "true");
            request.Content = JsonContent.Create(new { cardId = "2147483649", chipId = "00000A00010A0C00" });
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            CheckResponseFields(body);
            Check((int)response.StatusCode == 500 && !body.GetProperty("success").GetBoolean(), "Unexpected failures must return a compact failure response.");
        }
        return checks;
    }
}
