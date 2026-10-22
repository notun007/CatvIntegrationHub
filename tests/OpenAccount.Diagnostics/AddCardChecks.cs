using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatvIntegrationHub.Api;
using CatvIntegrationHub.Domain.Enums;

internal static class AddCardChecks
{
    public static async Task<int> RunAsync(HttpClient http, HscasOptions options, RecordingStub recorder)
    {
        int checks = 0;
        options.SendTimeoutMilliseconds = 5000;
        options.ReceiveTimeoutMilliseconds = 500;
        recorder.ThrowOnSave = false;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        async Task Scenario(uint clearResult, uint openResult, string clearTransport = "response",
            SvCardStatusEnum? failingSave = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int before = recorder.Records.Count;
            recorder.FailingStatus = failingSave;
            var server = Task.Run(async () =>
            {
                foreach (byte command in new byte[] { 0x1F, 0x1B })
                {
                    using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                    using var stream = peer.GetStream();
                    async Task<byte[]> Read(int length)
                    {
                        byte[] data = new byte[length];
                        for (int offset = 0; offset < length;)
                        {
                            int count = await stream.ReadAsync(data.AsMemory(offset), timeout.Token);
                            if (count == 0) throw new Exception("Incomplete AddCard request.");
                            offset += count;
                        }
                        return data;
                    }
                    byte[] header = await Read(6);
                    byte[] body = await Read(BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)));
                    Check(header[3] == command, "AddCard must clear before opening.");
                    Check(BinaryPrimitives.ReadUInt32BigEndian(body) == 3120913711, "Wrong AddCard CardId.");
                    if (command == 0x1B)
                        Check(Convert.ToHexString(body.AsSpan(10)) == "000000000734FF2F", "Wrong AddCard ChipId.");
                    if (command == 0x1F && clearTransport == "close") continue;
                    byte[] response = new byte[10];
                    header.CopyTo(response, 0);
                    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 4);
                    BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(6), command == 0x1F ? clearResult : openResult);
                    if (command == 0x1F && clearTransport == "malformed") response[3] = 0xFF;
                    await stream.WriteAsync(response, timeout.Token);
                }
            });
            try
            {
                using var response = await http.PostAsJsonAsync("/AddCard",
                    new { cardId = "3120913711", chipId = "000000000734ff2f" }, timeout.Token);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
                await server;
                bool openSaveFailure = openResult == 0 && failingSave == SvCardStatusEnum.OpenAccount;
                Check((int)response.StatusCode == (openSaveFailure ? 500 : 200), "AddCard HTTP status must come from OpenAccount.");
                Check(body.GetProperty("success").GetBoolean() == (openResult == 0 && !openSaveFailure), "Clearing must not determine AddCard success.");
                Check(body.GetProperty("reasonCode").GetString() == (openSaveFailure ? "OA501" : openResult == 0 ? "OA000" : "OA206"), "AddCard must return OpenAccount diagnostics.");
                var cas = openSaveFailure ? body.GetProperty("casResponse") : body;
                Check(cas.GetProperty("commandName").GetString() == "OpenAccount", "AddCard must return the OpenAccount response type.");
                Check(cas.GetProperty("resultHex").GetString() == openResult.ToString("X8"), "OpenAccount result must be preserved.");
                bool recordedClear = clearResult == 0 && clearTransport == "response" && failingSave != SvCardStatusEnum.ClearAuthorization;
                bool recordedOpen = openResult == 0 && !openSaveFailure;
                var records = recorder.Records.Skip(before).ToArray();
                Check(records.Length == (recordedClear ? 1 : 0) + (recordedOpen ? 1 : 0), "Wrong AddCard history count.");
                if (recordedClear) Check(records[0].Status == SvCardStatusEnum.ClearAuthorization, "Clear history must precede open history.");
                if (recordedOpen) Check(records[^1].Status == SvCardStatusEnum.OpenAccount, "Open history missing.");
                foreach (var record in records)
                {
                    var recorded = JsonDocument.Parse(record.Json!).RootElement;
                    Check(recorded.TryGetProperty("requestHex", out _) && recorded.TryGetProperty("responseHex", out _), "AddCard audit must retain packet details.");
                    Check(recorded.GetProperty("success").GetBoolean(), "AddCard must not record failed operations.");
                }
                Check(!listener.Pending(), "AddCard must not issue additional CAS operations.");
            }
            finally { listener.Stop(); recorder.FailingStatus = null; }
        }
        await Scenario(0, 0);
        await Scenario(0xFFFFFFFF, 0);
        await Scenario(0, 0xFFFFFFFF);
        await Scenario(0xFFFFFFFF, 0xFFFFFFFF);
        await Scenario(0, 0, "close");
        await Scenario(0, 0, "malformed");
        await Scenario(0, 0, failingSave: SvCardStatusEnum.ClearAuthorization);
        await Scenario(0, 0, failingSave: SvCardStatusEnum.OpenAccount);
        foreach (var input in new[]
        {
            ("{}", "OA100"), ("null", "OA103"), ("{", "OA103"),
            ("{\"cardId\":\"3120913711\"}", "OA104"),
            ("{\"cardId\":\"abc\",\"chipId\":\"000000000734ff2f\"}", "OA101"),
            ("{\"cardId\":\"0\",\"chipId\":\"000000000734ff2f\"}", "OA102"),
            ("{\"cardId\":\"3120913711\",\"chipId\":\"invalid\"}", "OA105"),
            ("{\"cardId\":\"3120913711\",\"chipId\":\"\"}", "OA104")
        })
        {
            int before = recorder.Count;
            using var response = await http.PostAsync("/AddCard", new StringContent(input.Item1, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Check((int)response.StatusCode == 400, "Invalid AddCard input must return 400.");
            Check(body.GetProperty("reasonCode").GetString() == input.Item2, "AddCard input diagnostics must match OpenAccount.");
            Check(!body.GetProperty("success").GetBoolean(), "Invalid AddCard input must fail.");
            Check(recorder.Count == before, "Invalid AddCard input must not clear or open a card.");
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/AddCard"))
        {
            request.Headers.Add("X-Test-Unexpected", "true");
            request.Content = JsonContent.Create(new { cardId = "3120913711", chipId = "000000000734ff2f" });
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Check((int)response.StatusCode == 500 && body.GetProperty("reasonCode").GetString() == "OA599", "AddCard middleware must use OpenAccount diagnostics.");
        }
        return checks;
    }
}
