using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using CatvIntegrationHub.Api;

internal static class QueryAuthorizationTimeChecks
{
    public static async Task<int> RunAsync(HttpClient http, HscasOptions options, RecordingStub recorder)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }

        recorder.ThrowOnSave = false;
        options.IpAddress = "127.0.0.1";
        options.SendTimeoutMilliseconds = 5000;
        options.ReceiveTimeoutMilliseconds = 5000;
        var bdt = TimeSpan.FromHours(6);
        var localBegin = new DateTimeOffset(2027, 11, 7, 0, 0, 0, bdt);
        var localEnd = new DateTimeOffset(2027, 11, 7, 23, 59, 59, bdt);
        var utcMidnightBoundary = new DateTimeOffset(2027, 11, 7, 23, 59, 59, TimeSpan.Zero);
        foreach (var (begin, end) in new[]
        {
            (localBegin, localEnd),
            (utcMidnightBoundary, utcMidnightBoundary.AddDays(1)),
            (localEnd, localEnd)
        })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            byte[] responsePacket = new byte[22];
            var server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = peer.GetStream();
                byte[] request = new byte[10];
                for (int offset = 0; offset < request.Length;)
                {
                    int read = await stream.ReadAsync(request.AsMemory(offset), timeout.Token);
                    if (read == 0) throw new Exception("Incomplete query request.");
                    offset += read;
                }
                Check(Convert.ToHexString(request.AsSpan(2, 4)) == "02200004", "Incorrect query header.");
                request.AsSpan(0, 6).CopyTo(responsePacket);
                BinaryPrimitives.WriteUInt16BigEndian(responsePacket.AsSpan(4), 16);
                BinaryPrimitives.WriteUInt16BigEndian(responsePacket.AsSpan(10), 1);
                BinaryPrimitives.WriteUInt16BigEndian(responsePacket.AsSpan(12), 1);
                BinaryPrimitives.WriteUInt32BigEndian(responsePacket.AsSpan(14), (uint)begin.ToUnixTimeSeconds());
                BinaryPrimitives.WriteUInt32BigEndian(responsePacket.AsSpan(18), (uint)end.ToUnixTimeSeconds());
                await stream.WriteAsync(responsePacket, timeout.Token);
            });
            try
            {
                using var response = await http.PostAsJsonAsync("/QueryAuthorization", new { cardId = "2147483649" }, timeout.Token);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
                await server;
                Check(response.IsSuccessStatusCode && body.GetProperty("success").GetBoolean(), "Query must succeed.");
                var record = body.GetProperty("authorizations")[0];
                Check(record.GetProperty("beginUtc").GetDateTimeOffset().EqualsExact(begin.ToUniversalTime()), "beginUtc changed.");
                Check(record.GetProperty("endUtc").GetDateTimeOffset().EqualsExact(end.ToUniversalTime()), "endUtc changed.");
                Check(record.GetProperty("beginLocal").GetDateTimeOffset().EqualsExact(begin.ToOffset(bdt)), "beginLocal must include the correct Bangladesh date and offset.");
                Check(record.GetProperty("endLocal").GetDateTimeOffset().EqualsExact(end.ToOffset(bdt)), "endLocal must include the correct Bangladesh date and offset.");
                Check(record.GetProperty("beginHex").GetString() == begin.ToUnixTimeSeconds().ToString("X8"), "Begin packet timestamp changed.");
                Check(record.GetProperty("endHex").GetString() == end.ToUnixTimeSeconds().ToString("X8"), "End packet timestamp changed.");
                Check(record.GetProperty("authorizationStatus").GetString() == (begin < end ? "Authorized" : "Unauthorized"), "Local conversion changed the record classification.");
                Check(body.GetProperty("responseHex").GetString() == Convert.ToHexString(responsePacket), "Raw CAS response changed.");
                var saved = JsonDocument.Parse(recorder.LastJson!).RootElement.GetProperty("authorizations")[0];
                Check(saved.GetProperty("beginLocal").GetDateTimeOffset().EqualsExact(begin.ToOffset(bdt)), "Saved response must include beginLocal.");
                Check(saved.GetProperty("endLocal").GetDateTimeOffset().EqualsExact(end.ToOffset(bdt)), "Saved response must include endLocal.");
            }
            finally { listener.Stop(); }
        }
        return checks;
    }
}
