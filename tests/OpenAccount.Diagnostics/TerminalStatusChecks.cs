using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatvIntegrationHub.Api;
using CatvIntegrationHub.Domain.Enums;

internal static class TerminalStatusChecks
{
    public static async Task<int> RunAsync(HttpClient http, HscasOptions options, RecordingStub recorder, int terminalStatus)
    {
        int checks = 0;
        string validRequest = JsonSerializer.Serialize(new { cardId = "2147483649", status = terminalStatus });
        recorder.ThrowOnSave = false;
        options.SendTimeoutMilliseconds = 5000;
        options.ReceiveTimeoutMilliseconds = 500;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        async Task<JsonElement> Post(string request, int status, string code)
        {
            using var response = await http.PostAsync("/tests/SetTerminalStatus", new StringContent(request, Encoding.UTF8, "application/json"));
            string text = await response.Content.ReadAsStringAsync();
            Check((int)response.StatusCode == status, $"Expected HTTP {status}: {text}");
            var body = JsonDocument.Parse(text).RootElement.Clone();
            Check(body.GetProperty("reasonCode").GetString() == code, $"Expected {code}: {text}");
            Check(body.GetProperty("success").GetBoolean() == (code == "TS000"), $"Incorrect success: {text}");
            Check(body.GetProperty("status").GetString() == (code == "TS000" ? "Success" : "Failure"), $"Incorrect status: {text}");
            return body;
        }
        async Task<JsonElement> Exchange(uint result, string code, int status = 200,
            byte[]? emm = null, Func<byte[], byte[]>? mutate = null, string transport = "response")
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int before = recorder.Count;
            var server = Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = peer.GetStream();
                byte[] request = new byte[16];
                int offset = 0;
                while (offset < request.Length)
                {
                    int read = await stream.ReadAsync(request.AsMemory(offset), timeout.Token);
                    if (read == 0) throw new Exception("Incomplete set-terminal-status request.");
                    offset += read;
                }
                Check(Convert.ToHexString(request.AsSpan(2, 4)) == "020B000A", "Incorrect set-terminal-status command header.");
                Check(Convert.ToHexString(request.AsSpan(6, 5)) == "8000000101", "Incorrect card ID or send-immediately flag.");
                Check(request[11] == terminalStatus, "Incorrect terminal status byte.");
                long expiry = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(12));
                Check(Math.Abs(expiry - DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds()) <= 5, "Terminal-status expiry must be now + three days.");
                if (transport == "close") return;
                if (transport == "timeout") { await Task.Delay(1200); return; }
                // Valid rejections may contain only Result. Success requires Emm_Count.
                byte[] extra = emm ?? (result == 0 ? new byte[] { 0 } : Array.Empty<byte>());
                byte[] response = new byte[10 + extra.Length];
                request.AsSpan(0, 2).CopyTo(response);
                response[2] = 2;
                response[3] = 0x0B;
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), (ushort)(4 + extra.Length));
                BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(6), result);
                extra.CopyTo(response, 10);
                if (mutate != null) response = mutate(response);
                await stream.WriteAsync(response.AsMemory(0, 3), timeout.Token);
                await stream.WriteAsync(response.AsMemory(3), timeout.Token);
            });
            try
            {
                var body = await Post(validRequest, status, code);
                await server;
                Check(recorder.Count == before + (code == "TS000" ? 1 : 0), "Only validated CAS success may be recorded.");
                if (code == "TS000" || code.StartsWith("TS2"))
                    Check(body.GetProperty("resultHex").GetString() == result.ToString("X8"), "CAS result must be preserved.");
                return body;
            }
            finally { listener.Stop(); }
        }

        var success = await Exchange(0, "TS000");
        Check(success.GetProperty("reason").GetString() == (terminalStatus == 1 ? "Terminal activated successfully." : "Terminal inactivated successfully."), "Incorrect success message.");
        Check(recorder.LastJson!.Contains("TS000"), "Saved JSON must include SetTerminalStatus diagnostics.");
        Check(recorder.LastStatus == (terminalStatus == 1 ? SvCardStatusEnum.ActiveTerminal : SvCardStatusEnum.InactiveTerminal), "SetTerminalStatus must save the correct operation status.");
        await Exchange(0, "TS000", emm: new byte[] { 2, 2, 0xAB, 0xCD, 1, 0xEF });
        foreach (var error in new (uint Result, string Code)[]
        {
            (0xD0000002, "TS201"), (0xD0000004, "TS202"), (0xD0000005, "TS203"),
            (0xD000000A, "TS204"), (0xD000001E, "TS205"), (0xFFFFFFFF, "TS206"),
            (0xD000001B, "TS207"), (0xD000000B, "TS208"), (0xD000000F, "TS209"), (0x12345678, "TS299")
        }) await Exchange(error.Result, error.Code);
        await Exchange(0, "TS401", 502, mutate: b => { b[1] ^= 1; return b; });
        await Exchange(0, "TS402", 502, mutate: b => { b[3] = 0x1B; return b; });
        await Exchange(0, "TS403", 502, mutate: b => { b[2] = 1; return b; });
        await Exchange(0, "TS400", 502, mutate: b => { b[5] = 3; return b[..9]; });
        await Exchange(0, "TS404", 502, emm: Array.Empty<byte>());
        await Exchange(0, "TS404", 502, emm: new byte[] { 1 });
        await Exchange(0, "TS404", 502, emm: new byte[] { 1, 2, 0xAB });
        await Exchange(0, "TS404", 502, emm: new byte[] { 0, 0xAB });
        await Exchange(0, "TS404", 502, emm: new byte[] { 2, 1, 0xAB });
        await Exchange(0, "TS304", 502, transport: "close");
        await Exchange(0, "TS304", 502, mutate: b => b[..8]);
        await Exchange(0, "TS303", 504, transport: "timeout");

        int beforeErrors = recorder.Count;
        foreach (string request in new[] { "", "{", "null" }) await Post(request, 400, "TS103");
        await Post("{}", 400, "TS100");
        await Post("{\"cardId\":null}", 400, "TS100");
        await Post(validRequest.Replace("2147483649", " "), 400, "TS100");
        await Post(validRequest.Replace("2147483649", "abc"), 400, "TS101");
        foreach (string id in new[] { "0", "4294967295", "4294967296" })
            await Post(validRequest.Replace("2147483649", id), 400, "TS102");
        await Post("{\"cardId\":\"1\"}", 400, "TS104");
        foreach (string status in new[] { "null", "2", "-1", "\"invalid\"" })
            await Post("{\"cardId\":\"1\",\"status\":" + status + "}", 400, "TS104");
        var client = new HscasClient(Microsoft.Extensions.Options.Options.Create(options));
        Check(client.SetTerminalStatus("1", 2).ReasonCode == "TS104", "Direct client calls must also reject invalid status.");
        Check(client.SetTerminalStatus("1", -1).HttpStatusCode == 400, "Direct client validation must return HTTP 400 diagnostics.");
        options.IpAddress = "invalid";
        await Post(validRequest, 500, "TS500");
        options.IpAddress = "127.0.0.1";
        options.Port = 0;
        await Post(validRequest, 500, "TS500");
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        options.Port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await Post(validRequest, 502, "TS300");
        Check(recorder.Count == beforeErrors, "Input/transport failures must not be recorded.");

        recorder.ThrowOnSave = true;
        var saveFailure = await Exchange(0, "TS501", 500);
        Check(saveFailure.GetProperty("casSucceeded").GetBoolean(), "Save failure must preserve CAS success.");
        Check(!saveFailure.GetProperty("databaseSaveConfirmed").GetBoolean(), "Save failure must be explicit.");
        Check(saveFailure.GetProperty("casResponse").GetProperty("reasonCode").GetString() == "TS000", "Nested CAS response must retain success.");
        using var unexpected = new HttpRequestMessage(HttpMethod.Post, "/SetTerminalStatus");
        unexpected.Headers.Add("X-Test-Unexpected", "true");
        unexpected.Content = new StringContent(validRequest, Encoding.UTF8, "application/json");
        using var failure = await http.SendAsync(unexpected);
        var failureBody = await failure.Content.ReadFromJsonAsync<JsonElement>();
        Check(failure.StatusCode == HttpStatusCode.InternalServerError, "Unexpected failure must return HTTP 500.");
        Check(failureBody.GetProperty("reasonCode").GetString() == "TS599", "Unexpected failure needs SetTerminalStatus diagnostics.");
        Check(!failureBody.GetProperty("success").GetBoolean(), "Unexpected failure must not report success.");
        using var hidden = await http.PostAsync("/SetTerminalStatus", new StringContent(validRequest, Encoding.UTF8, "application/json"));
        Check(hidden.StatusCode == HttpStatusCode.NotFound, "Production SetTerminalStatus must remain private.");
        Check(typeof(CatvIntegrationHub.Api.Controllers.HscasController).GetMethod("SetTerminalStatus",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) == null,
            "Production SetTerminalStatus must not become public.");
        return checks;
    }
}
