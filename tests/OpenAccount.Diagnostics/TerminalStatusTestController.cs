using System.Reflection;
using CatvIntegrationHub.Api;
using CatvIntegrationHub.Api.Controllers;
using CatvIntegrationHub.Application.Cards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// Test-only HTTP adapter: exercises private production action and its model-state filter
// without exposing /SetTerminalStatus in the production application.
[ApiController]
[Route("tests/SetTerminalStatus")]
public sealed class TerminalStatusTestController : ControllerBase
{
    private readonly HscasController _controller;
    private static readonly MethodInfo Action = typeof(HscasController).GetMethod("SetTerminalStatus",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    public TerminalStatusTestController(HscasClient client, ISvCardOperationRecorder recorder,
        ILogger<HscasController> logger, IOptions<JsonOptions> jsonOptions)
    {
        _controller = new HscasController(client, recorder, logger, jsonOptions);
    }

    [HttpPost]
    [SetTerminalStatusDiagnosticsFilter]
    public Task<ActionResult<SetTerminalStatusResponse>> Execute(SetTerminalStatusRequest request)
    {
        _controller.ControllerContext = ControllerContext;
        return (Task<ActionResult<SetTerminalStatusResponse>>)Action.Invoke(_controller, new object[] { request })!;
    }
}
