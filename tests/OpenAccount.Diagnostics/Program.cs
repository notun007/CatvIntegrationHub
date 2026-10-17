using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatvIntegrationHub.Api;
using CatvIntegrationHub.Api.Controllers;
using CatvIntegrationHub.Api.Middleware;
using CatvIntegrationHub.Application.Cards;
using CatvIntegrationHub.Domain.Enums;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

// Dependency-free integration runner: real HTTP pipeline and loopback CAS, no live CAS/database.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
var casOptions = new HscasOptions { IpAddress = "127.0.0.1", SendTimeoutMilliseconds = 500, ReceiveTimeoutMilliseconds = 500 };
var recorder = new RecordingStub();
builder.Services.AddSingleton<IOptions<HscasOptions>>(Options.Create(casOptions));
builder.Services.AddSingleton<HscasClient>();
builder.Services.AddSingleton<ISvCardOperationRecorder>(recorder);
builder.Services.AddControllers().AddApplicationPart(typeof(HscasController).Assembly);
await using var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.MapControllers();
// Exercise the middleware fallback independently of predictable client failures.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("X-Test-Unexpected"))
        throw new InvalidOperationException("Simulated unexpected failure.");
    await next();
});
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
const string validRequest = "{\"cardId\":\"2147483649\",\"chipId\":\"00000A00010A0C00\"}";
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

async Task<JsonElement> Post(string request, int status, string reasonCode)
{
    using var response = await http.PostAsync("/OpenAccount", new StringContent(request, Encoding.UTF8, "application/json"));
    string text = await response.Content.ReadAsStringAsync();
    Check((int)response.StatusCode == status, $"Expected HTTP {status}, got {response.StatusCode}: {text}");
    var body = JsonDocument.Parse(text).RootElement.Clone();
    Check(body.GetProperty("reasonCode").GetString() == reasonCode, $"Expected {reasonCode}: {text}");
    bool success = reasonCode == "OA000";
    Check(body.GetProperty("success").GetBoolean() == success, $"Incorrect success: {text}");
    Check(body.GetProperty("status").GetString() == (success ? "Success" : "Failure"), $"Incorrect status: {text}");
    return body;
}

async Task<JsonElement> Exchange(uint result, string reasonCode, int status = 200,
    Func<byte[], byte[]>? mutate = null, string transport = "response")
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    casOptions.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    int before = recorder.Count;
    bool recorded = result == 0 && reasonCode == "OA000";
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
            if (read == 0) throw new Exception("Incomplete request.");
            offset += read;
        }
        Check(Convert.ToHexString(request.AsSpan(2, 4)) == "021B0012", "Incorrect open-account command header.");
        Check(Convert.ToHexString(request.AsSpan(6, 4)) == "80000001", "Incorrect card ID.");
        Check(Convert.ToHexString(request.AsSpan(14)) == "010800000A00010A0C00", "Incorrect chip fields.");
        long expiry = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(10, 4));
        Check(Math.Abs(expiry - DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()) <= 5, "Expiry must be now + one day.");
        if (transport == "close") return;
        if (transport == "timeout") { await Task.Delay(1200); return; }
        byte[] response = { request[0], request[1], 2, 0x1B, 0, 4, 0, 0, 0, 0 };
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(6), result);
        if (mutate != null) response = mutate(response);
        // Deliberately fragment the packet to exercise ReceiveExactly.
        await stream.WriteAsync(response.AsMemory(0, 3), timeout.Token);
        await stream.WriteAsync(response.AsMemory(3), timeout.Token);
    });
    try
    {
        var body = await Post(validRequest, status, reasonCode);
        await server;
        Check(recorder.Count == before + (recorded ? 1 : 0), "Only successful CAS openings should be recorded.");
        if (reasonCode.StartsWith("OA2") || reasonCode == "OA000")
            Check(body.GetProperty("resultHex").GetString() == result.ToString("X8"), "CAS result must be preserved.");
        return body;
    }
    finally { listener.Stop(); }
}

await Exchange(0, "OA000");
Check(recorder.LastJson!.Contains("OA000"), "Saved JSON must include diagnostics.");
var errors = new (uint Result, string Code)[]
{
    (0xD0000002, "OA201"), (0xD0000004, "OA202"), (0xD0000005, "OA203"),
    (0xD000000A, "OA204"), (0xD000001E, "OA205"), (0xFFFFFFFF, "OA206"),
    (0xD000001B, "OA207"), (0xD000000B, "OA208"), (0xD000001F, "OA209"),
    (0xD0000020, "OA210"), (0x12345678, "OA299")
};
foreach (var error in errors) await Exchange(error.Result, error.Code);
await Exchange(0, "OA401", 502, b => { b[1] ^= 1; return b; });
await Exchange(0, "OA402", 502, b => { b[3] = 0x20; return b; });
await Exchange(0, "OA403", 502, b => { b[2] = 1; return b; });
await Exchange(0, "OA400", 502, b => { b[5] = 3; return b[..9]; });
await Exchange(0, "OA400", 502, b => { b[5] = 5; return b.Concat(new byte[] { 0 }).ToArray(); });
await Exchange(0, "OA304", 502, transport: "close");
await Exchange(0, "OA304", 502, b => { b[5] = 4; return b[..8]; });
await Exchange(0, "OA303", 504, transport: "timeout");

int countBeforeInputErrors = recorder.Count;
await Post("{}", 400, "OA100");
await Post("", 400, "OA103");
await Post("{", 400, "OA103");
await Post("null", 400, "OA103");
await Post("{\"cardId\":\"1\"}", 400, "OA104");
foreach (string id in new[] { "0", "4294967295", "4294967296" })
    await Post(validRequest.Replace("2147483649", id), 400, "OA102");
await Post(validRequest.Replace("2147483649", "abc"), 400, "OA101");
await Post(validRequest.Replace("2147483649", " "), 400, "OA100");
await Post(validRequest.Replace("00000A00010A0C00", " "), 400, "OA104");
await Post(validRequest.Replace("00000A00010A0C00", "123"), 400, "OA105");
await Post(validRequest.Replace("00000A00010A0C00", "00000G00010A0C00"), 400, "OA105");
casOptions.IpAddress = "invalid";
await Post(validRequest, 500, "OA500");
casOptions.IpAddress = "127.0.0.1";
casOptions.Port = 0;
await Post(validRequest, 500, "OA500");
var closedListener = new TcpListener(IPAddress.Loopback, 0);
closedListener.Start();
casOptions.Port = ((IPEndPoint)closedListener.LocalEndpoint).Port;
closedListener.Stop();
// Windows can take over a second to report refusal; keep this distinct from a connect timeout.
casOptions.SendTimeoutMilliseconds = 5000;
await Post(validRequest, 502, "OA300");
Check(recorder.Count == countBeforeInputErrors, "Invalid requests and connection failures must not be recorded.");

recorder.ThrowOnSave = true;
var saveFailure = await Exchange(0, "OA501", 500);
Check(saveFailure.GetProperty("casSucceeded").GetBoolean(), "Save failure must preserve CAS success.");
Check(!saveFailure.GetProperty("databaseSaveConfirmed").GetBoolean(), "Save failure must be explicit.");
Check(saveFailure.GetProperty("casResponse").GetProperty("reasonCode").GetString() == "OA000", "Nested CAS result must retain success.");
using (var request = new HttpRequestMessage(HttpMethod.Post, "/OpenAccount"))
{
    request.Headers.Add("X-Test-Unexpected", "true");
    request.Content = new StringContent(validRequest, Encoding.UTF8, "application/json");
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Check(response.StatusCode == HttpStatusCode.InternalServerError, "Unexpected failure must return HTTP 500.");
    Check(body.GetProperty("reasonCode").GetString() == "OA599", "Unexpected failure needs diagnostics.");
    Check(!body.GetProperty("success").GetBoolean(), "Unexpected failure must not return success.");
}
Console.WriteLine($"PASS: {checks} assertions across OpenAccount HTTP, CAS packet, validation, transport, and persistence scenarios.");
int closeChecks = await CloseAccountChecks.RunAsync(http, casOptions, recorder);
Console.WriteLine($"PASS: {closeChecks} assertions across CloseAccount HTTP, CAS packet, EMM, validation, transport, and persistence scenarios.");
foreach (bool unauthorize in new[] { false, true })
{
    int authorizationChecks = await AuthorizationChecks.RunAsync(http, casOptions, recorder, unauthorize);
    Console.WriteLine($"PASS: {authorizationChecks} assertions across {(unauthorize ? "UnAuthorize" : "Authorize")} HTTP, CAS packet, validation, transport, and persistence scenarios.");
}
int clearChecks = await ClearAuthorizationChecks.RunAsync(http, casOptions, recorder);
Console.WriteLine($"PASS: {clearChecks} assertions across ClearAuthorization HTTP, CAS packet, validation, transport, and persistence scenarios.");
foreach (int terminalStatus in new[] { 0, 1 })
{
    int terminalChecks = await TerminalStatusChecks.RunAsync(http, casOptions, recorder, terminalStatus);
    Console.WriteLine($"PASS: {terminalChecks} assertions across private SetTerminalStatus ({terminalStatus}), CAS packet, EMM, validation, transport, and persistence scenarios.");
}
await app.StopAsync();

sealed class RecordingStub : ISvCardOperationRecorder
{
    public int Count { get; private set; }
    public string? LastJson { get; private set; }
    public SvCardStatusEnum LastStatus { get; private set; }
    public bool ThrowOnSave { get; set; }
    public Task RecordAsync(string cardId, string? chipId, SvCardStatusEnum status,
        string? productId = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null,
        string? requestHex = null, string? responseHex = null, string? responseJson = null,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave) throw new IOException("Simulated database failure.");
        Count++;
        LastJson = responseJson;
        LastStatus = status;
        return Task.CompletedTask;
    }
}
