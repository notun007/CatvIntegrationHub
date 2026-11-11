using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatvIntegrationHub.Api;
using CatvIntegrationHub.Domain.Enums;

internal static class AuthorizationChecks
{
    public static async Task<int> RunAsync(HttpClient http, HscasOptions options, RecordingStub recorder, bool unauthorize)
    {
        int checks = 0;
        string prefix = unauthorize ? "UA" : "AU";
        string endpoint = unauthorize ? "/UnAuthorize" : "/Authorize";
        DateTimeOffset startTime = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset endTime = DateTimeOffset.UtcNow.AddDays(1);
        string MakeRequest(string? product = "257", DateTimeOffset? start = null, DateTimeOffset? end = null) =>
            JsonSerializer.Serialize(new { cardId = "2147483649", productId = product, startTime = start ?? startTime, endTime = end ?? endTime });
        string validRequest = MakeRequest();
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
            using var response = await http.PostAsync(endpoint, new StringContent(request, Encoding.UTF8, "application/json"));
            string text = await response.Content.ReadAsStringAsync();
            Check((int)response.StatusCode == status, $"Expected HTTP {status}: {text}");
            var body = JsonDocument.Parse(text).RootElement.Clone();
            Check(body.GetProperty("reasonCode").GetString() == code, $"Expected {code}: {text}");
            Check(body.GetProperty("success").GetBoolean() == (code == prefix + "000"), $"Incorrect success: {text}");
            Check(body.GetProperty("status").GetString() == (code == prefix + "000" ? "Success" : "Failure"), $"Incorrect status: {text}");
            return body;
        }
        async Task<JsonElement> Exchange(uint result, string code, int status = 200,
            byte[]? returnedData = null, Func<byte[], byte[]>? mutate = null, string transport = "response", string? requestBody = null,
            DateTimeOffset? expectedStart = null, DateTimeOffset? expectedEnd = null)
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
                byte[] request = new byte[24];
                int offset = 0;
                while (offset < request.Length)
                {
                    int read = await stream.ReadAsync(request.AsMemory(offset), timeout.Token);
                    if (read == 0) throw new Exception("Incomplete authorization request.");
                    offset += read;
                }
                Check(Convert.ToHexString(request.AsSpan(2, 4)) == "02010012", "Incorrect authorization command header.");
                Check(Convert.ToHexString(request.AsSpan(6, 9)) == "800000010101000101", "Incorrect card/product/flag fields.");
                Check(BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(15, 4)) == (unauthorize ? expectedEnd ?? endTime : expectedStart ?? startTime).ToUnixTimeSeconds(), "Incorrect begin time or un-authorization encoding.");
                Check(BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(19, 4)) == (expectedEnd ?? endTime).ToUnixTimeSeconds(), "Incorrect end time.");
                Check(request[23] == 0, "Description length must be zero.");
                if (transport == "close") return;
                if (transport == "timeout") { await Task.Delay(1200); return; }
                // Valid rejections may contain only Result. Success requires ReturnAmount.
                byte[] extra = returnedData ?? (result == 0 ? new byte[] { 0 } : Array.Empty<byte>());
                byte[] response = new byte[10 + extra.Length];
                request.AsSpan(0, 2).CopyTo(response);
                response[2] = 2;
                response[3] = 0x01;
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), (ushort)(4 + extra.Length));
                BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(6), result);
                extra.CopyTo(response, 10);
                if (mutate != null) response = mutate(response);
                await stream.WriteAsync(response.AsMemory(0, 3), timeout.Token);
                await stream.WriteAsync(response.AsMemory(3), timeout.Token);
            });
            try
            {
                var body = await Post(requestBody ?? validRequest, status, code);
                await server;
                Check(recorder.Count == before + (code == prefix + "000" ? 1 : 0), "Only validated CAS success may be recorded.");
                if (code == prefix + "000" || code.StartsWith(prefix + "2"))
                    Check(body.GetProperty("resultHex").GetString() == result.ToString("X8"), "CAS result must be preserved.");
                return body;
            }
            finally { listener.Stop(); }
        }

        var success = await Exchange(0, prefix + "000");
        Check(success.GetProperty("reason").GetString() == (unauthorize ? "Product unauthorized successfully." : "Product authorized successfully."), "Incorrect success message.");
        Check(recorder.LastJson!.Contains(prefix + "000"), "Saved JSON must include authorization diagnostics.");
        Check(recorder.LastStatus == (unauthorize ? SvCardStatusEnum.Unauthorize : SvCardStatusEnum.Authorize), "Authorization must save the correct operation status.");
        await Exchange(0, prefix + "000", returnedData: new byte[] { 2, 1, 1, 2, 0xAB, 0xCD, 1, 2, 1, 0xEF });
        await Exchange(0, prefix + "000", returnedData: new byte[] { 1, 1, 1, 0 });
        await Exchange(0, prefix + "000", requestBody: MakeRequest(product: "0x0101"));
        // Preserve this reported UTC value through HTTP binding, packet encoding and recording.
        var reportedStart = new DateTimeOffset(2026, 12, 6, 11, 59, 59, TimeSpan.Zero);
        var reportedEnd = reportedStart.AddDays(1);
        foreach (string utcSuffix in new[] { "+00:00", "Z" })
        {
            string reportedRequest = JsonSerializer.Serialize(new
            {
                cardId = "2147483649", productId = "0x0001",
                startTime = "2026-12-06T11:59:59" + utcSuffix,
                endTime = "2026-12-07T11:59:59" + utcSuffix
            });
            // Exchange checks product 0x0101, independently of the date regression.
            reportedRequest = reportedRequest.Replace("0x0001", "0x0101");
            await Exchange(0, prefix + "000", requestBody: reportedRequest,
                expectedStart: reportedStart, expectedEnd: reportedEnd);
            Check(recorder.LastStartTime?.EqualsExact(reportedStart) == true,
                "HTTP/controller must preserve 2026-12-06T11:59:59+00:00, including its UTC offset.");
            Check(recorder.LastEndTime?.EqualsExact(reportedEnd) == true,
                "HTTP/controller must preserve the end time and its UTC offset.");
            Console.WriteLine($"PASS: {endpoint} {utcSuffix} input retained {recorder.LastStartTime:O} through HTTP, CAS packet and recording.");
        }
        // Equivalent offsets must encode the same instant, without a six-hour shift.
        foreach (var offset in new[] { TimeSpan.FromHours(6), TimeSpan.FromHours(-5), TimeSpan.FromMinutes(345) })
            await Exchange(0, prefix + "000", requestBody: MakeRequest(start: startTime.ToOffset(offset), end: endTime.ToOffset(offset)));
        var nearStart = DateTimeOffset.UtcNow.AddMinutes(-5).ToOffset(TimeSpan.FromHours(6));
        var nearEnd = DateTimeOffset.UtcNow.AddHours(2).ToOffset(TimeSpan.FromHours(6));
        await Exchange(0, prefix + "000", requestBody: MakeRequest(start: nearStart, end: nearEnd),
            expectedStart: nearStart, expectedEnd: nearEnd);
        foreach (var error in new (uint Result, string Code)[]
        {
            (0xD0000002, prefix + "201"), (0xD0000004, prefix + "202"), (0xD0000005, prefix + "203"),
            (0xD000000A, prefix + "204"), (0xD000001E, prefix + "205"), (0xFFFFFFFF, prefix + "206"),
            (0xD000001B, prefix + "207"), (0xD0000022, prefix + "211"), (0x12345678, prefix + "299")
        }) await Exchange(error.Result, error.Code);
        await Exchange(0, prefix + "401", 502, mutate: b => { b[1] ^= 1; return b; });
        await Exchange(0, prefix + "402", 502, mutate: b => { b[3] = 0x1B; return b; });
        await Exchange(0, prefix + "403", 502, mutate: b => { b[2] = 1; return b; });
        await Exchange(0, prefix + "400", 502, mutate: b => { b[5] = 3; return b[..9]; });
        await Exchange(0, prefix + "404", 502, returnedData: Array.Empty<byte>());
        await Exchange(0, prefix + "404", 502, returnedData: new byte[] { 1 });
        await Exchange(0, prefix + "404", 502, returnedData: new byte[] { 1, 1, 1, 2, 0xAB });
        await Exchange(0, prefix + "404", 502, returnedData: new byte[] { 0, 0xAB });
        await Exchange(0, prefix + "404", 502, returnedData: new byte[] { 2, 1, 1, 1, 0xAB });
        await Exchange(0, prefix + "304", 502, transport: "close");
        await Exchange(0, prefix + "304", 502, mutate: b => b[..8]);
        await Exchange(0, prefix + "303", 504, transport: "timeout");

        int beforeErrors = recorder.Count;
        foreach (string request in new[] { "", "{", "null" }) await Post(request, 400, prefix + "103");
        await Post("{}", 400, prefix + "100");
        await Post("{\"cardId\":null}", 400, prefix + "100");
        await Post(validRequest.Replace("2147483649", " "), 400, prefix + "100");
        await Post(validRequest.Replace("2147483649", "abc"), 400, prefix + "101");
        foreach (string id in new[] { "0", "4294967295", "4294967296" })
            await Post(validRequest.Replace("2147483649", id), 400, prefix + "102");
        await Post("{\"cardId\":\"1\"}", 400, prefix + "104");
        await Post(MakeRequest(product: null), 400, prefix + "104");
        await Post(MakeRequest(product: " "), 400, prefix + "104");
        foreach (string product in new[] { "0", "65536", "0x10000", "XYZ", "0xZZ" })
            await Post(MakeRequest(product: product), 400, prefix + "105");
        await Post(MakeRequest(start: endTime, end: startTime), 400, prefix + "106");
        await Post(MakeRequest(start: endTime), 400, prefix + "106");
        var sameSecond = DateTimeOffset.FromUnixTimeSeconds(endTime.ToUnixTimeSeconds());
        await Post(MakeRequest(start: sameSecond.AddMilliseconds(100), end: sameSecond.AddMilliseconds(900)), 400, prefix + "106");
        await Post(MakeRequest(start: startTime.AddDays(-2), end: startTime), 400, prefix + "107");
        await Post(MakeRequest(start: startTime.AddDays(-2).ToOffset(TimeSpan.FromHours(6)), end: startTime.ToOffset(TimeSpan.FromHours(6))), 400, prefix + "107");
        await Post(MakeRequest(start: endTime.ToOffset(TimeSpan.FromHours(-5)), end: startTime.ToOffset(TimeSpan.FromHours(6))), 400, prefix + "106");
        foreach (string field in new[] { "startTime", "endTime" })
        {
            foreach (string invalid in new[] { "2027-11-04T22:28:00", "2027-11-04", "2027-02-30T22:28:00+06:00", "2027-11-04T22:28:00+25:00" })
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(validRequest)!;
                values[field] = JsonSerializer.SerializeToElement(invalid);
                await Post(JsonSerializer.Serialize(values), 400, prefix + "103");
            }
        }
        await Post(MakeRequest(start: DateTimeOffset.UnixEpoch.AddSeconds(-1)), 400, prefix + "108");
        await Post(MakeRequest(end: DateTimeOffset.FromUnixTimeSeconds((long)uint.MaxValue + 1)), 400, prefix + "108");
        await Post(validRequest.Replace("startTime", "unusedTime"), 400, prefix + "108");
        await Post("{\"cardId\":\"1\",\"productId\":\"1\",\"startTime\":\"invalid\"}", 400, prefix + "103");
        options.IpAddress = "invalid";
        await Post(validRequest, 500, prefix + "500");
        options.IpAddress = "127.0.0.1";
        options.Port = 0;
        await Post(validRequest, 500, prefix + "500");
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        options.Port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await Post(validRequest, 502, prefix + "300");
        Check(recorder.Count == beforeErrors, "Input/transport failures must not be recorded.");

        recorder.ThrowOnSave = true;
        var saveFailure = await Exchange(0, prefix + "501", 500);
        Check(saveFailure.GetProperty("casSucceeded").GetBoolean(), "Save failure must preserve CAS success.");
        Check(!saveFailure.GetProperty("databaseSaveConfirmed").GetBoolean(), "Save failure must be explicit.");
        Check(saveFailure.GetProperty("casResponse").GetProperty("reasonCode").GetString() == prefix + "000", "Nested CAS response must retain success.");
        using var unexpected = new HttpRequestMessage(HttpMethod.Post, endpoint);
        unexpected.Headers.Add("X-Test-Unexpected", "true");
        unexpected.Content = new StringContent(validRequest, Encoding.UTF8, "application/json");
        using var failure = await http.SendAsync(unexpected);
        var failureBody = await failure.Content.ReadFromJsonAsync<JsonElement>();
        Check(failure.StatusCode == HttpStatusCode.InternalServerError, "Unexpected failure must return HTTP 500.");
        Check(failureBody.GetProperty("reasonCode").GetString() == prefix + "599", "Unexpected failure needs authorization diagnostics.");
        Check(!failureBody.GetProperty("success").GetBoolean(), "Unexpected failure must not report success.");
        return checks;
    }
}
