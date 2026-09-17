using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Tests.Support;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Agents;

/// <summary>Guardia sui tool (3.9): allow-list, parametri di create_order dal codice, fatti del run, span tool.call.</summary>
public class GuardedToolFunctionTests
{
    private FakeO2CTools _tools = null!;
    private DealRunContext _context = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [SetUp]
    public void CreateContext()
    {
        _tools = new FakeO2CTools();
        _context = new DealRunContext("D-1001", "corr-guard-test", SingleOrderAgent.AgentName, SingleOrderAgent.AllowedTools);
    }

    [Test]
    public void CreateOrder_Schema_HidesInjectedArguments()
    {
        //SETUP
        var createOrder = Guard(AgentToolNames.CreateOrder);

        //SUT
        var schema = createOrder.JsonSchema;

        Assert.That(PropertyNames(schema), Is.EquivalentTo(new[] { "customerId", "lines" }));
        Assert.That(schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()), Is.EquivalentTo(new[] { "customerId", "lines" }));
    }

    [Test]
    public void OtherTools_Schema_IsUnchanged()
    {
        //SETUP
        var inner = _tools.All().Single(t => t.QualifiedName == AgentToolNames.UpdateDeal).Function;

        //SUT
        var schema = Guard(AgentToolNames.UpdateDeal).JsonSchema;

        Assert.That(schema.GetRawText(), Is.EqualTo(inner.JsonSchema.GetRawText()));
    }

    [Test]
    public async Task CreateOrder_ModelProposesKeyAndRef_GuardOverridesWithDealValues()
    {
        //SETUP
        await Guard(AgentToolNames.GetDeal).InvokeAsync(new AIFunctionArguments { ["dealId"] = "D-1001" }, CancellationToken);

        var arguments = new AIFunctionArguments
        {
            ["customerId"] = 1,
            ["lines"] = new[] { new { sku = "IND-BRG-001", quantity = 40, unitPrice = 12m } },
            ["idempotencyKey"] = "invented-by-the-model",
            ["externalRef"] = "D-9999"
        };

        //SUT
        await Guard(AgentToolNames.CreateOrder).InvokeAsync(arguments, CancellationToken);

        Assert.That(_tools.CreateOrderArguments?["idempotencyKey"], Is.EqualTo("o2c-D-1001-r3"));
        Assert.That(_tools.CreateOrderArguments?["externalRef"], Is.EqualTo("D-1001"));
        Assert.That(_context.Order?.OrderNumber, Is.EqualTo(FakeO2CTools.OrderNumber));
    }

    [Test]
    public async Task CreateOrder_BeforeGetDeal_ReturnsValidationErrorWithoutCallingTool()
    {
        //SETUP
        var arguments = new AIFunctionArguments { ["customerId"] = 1, ["lines"] = Array.Empty<object>() };

        //SUT
        var result = await Guard(AgentToolNames.CreateOrder).InvokeAsync(arguments, CancellationToken);

        Assert.That(ErrorCode(result), Is.EqualTo(ToolErrorCodes.ValidationError));
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
    }

    [Test]
    public async Task ToolOutsideAllowList_ReturnsUnauthorizedWithoutCallingTool()
    {
        //SETUP
        var context = new DealRunContext("D-1001", "corr-guard-test", "ReadOnlyAgent", new HashSet<string> { AgentToolNames.GetDeal });
        var createOrder = new GuardedToolFunction(_tools.All().Single(t => t.QualifiedName == AgentToolNames.CreateOrder), context);

        //SUT
        var result = await createOrder.InvokeAsync(new AIFunctionArguments { ["customerId"] = 1 }, CancellationToken);

        Assert.That(ErrorCode(result), Is.EqualTo(ToolErrorCodes.Unauthorized));
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
        Assert.That(context.ToolCalls.Single().Outcome, Is.EqualTo("error:UNAUTHORIZED"));
    }

    [TestCase(AgentToolNames.UpdateDeal)]
    [TestCase(AgentToolNames.CreateOrder)]
    [TestCase(AgentToolNames.CreateCustomer)]
    public async Task WriteTool_AfterAStopVerdict_IsRejectedForAgentsButNotForTheHost(string tool)
    {
        //SETUP
        var context = new DealRunContext("D-1001", "corr-guard-test", AgentScope.HostName, new HashSet<string>());
        var agentScope = new AgentScope("OrderAgent", new HashSet<string> { tool });
        var hostScope = new AgentScope(AgentScope.HostName, new HashSet<string> { tool });
        var inner = _tools.All().Single(t => t.QualifiedName == tool);

        await new GuardedToolFunction(_tools.All().Single(t => t.QualifiedName == AgentToolNames.GetDeal), context, hostScope with
        {
            AllowedTools = new HashSet<string> { AgentToolNames.GetDeal }
        }).InvokeAsync(new AIFunctionArguments { ["dealId"] = "D-1001" }, CancellationToken);

        await WorkflowAgents.CreateVerdictTool(WorkflowAgents.Fulfillment, context)
            .InvokeAsync(new AIFunctionArguments { ["reason"] = "SKU inesistente" }, CancellationToken);

        var arguments = new AIFunctionArguments
        {
            ["dealId"] = "D-1001",
            ["status"] = "Failed",
            ["customerId"] = 1,
            ["lines"] = new[] { new { sku = "IND-BRG-001", quantity = 1, unitPrice = 12m } },
            ["name"] = "Nuovo cliente",
            ["vatNumber"] = "IT00000000000",
            ["email"] = "a@b.it",
            ["address"] = "Via Roma 1"
        };

        //SUT
        var agentResult = await new GuardedToolFunction(inner, context, agentScope).InvokeAsync(arguments, CancellationToken);
        var hostResult = await new GuardedToolFunction(inner, context, hostScope).InvokeAsync(arguments, CancellationToken);

        Assert.That(ErrorCode(agentResult), Is.EqualTo(ToolErrorCodes.Conflict));
        Assert.That(ErrorCode(hostResult), Is.Null, "l'host scrive l'esito anche dopo l'arresto");
        Assert.That(_tools.CreateOrderCalls + _tools.CreateCustomerCalls + _tools.UpdatedStatuses.Count, Is.EqualTo(1),
            "solo la chiamata dell'host raggiunge il tool");
    }

    [Test]
    public async Task ReadTool_AfterAStopVerdict_IsStillAllowed()
    {
        //SETUP
        await WorkflowAgents.CreateVerdictTool(WorkflowAgents.Fulfillment, _context)
            .InvokeAsync(new AIFunctionArguments { ["reason"] = "SKU inesistente" }, CancellationToken);

        //SUT
        var result = await Guard(AgentToolNames.GetDeal).InvokeAsync(new AIFunctionArguments { ["dealId"] = "D-1001" }, CancellationToken);

        Assert.That(ErrorCode(result), Is.Null);
        Assert.That(_context.Deal, Is.Not.Null);
    }

    [Test]
    public async Task McpErrorResult_IsRecordedAsErrorAndNotAsFact()
    {
        //SETUP
        const string mcpError = """{"content":[{"type":"text","text":"{\"error\":{\"code\":\"NOT_FOUND\",\"message\":\"Deal D-1001 non trovato.\"}}"}],"isError":true}""";
        var getDeal = new AgentTool(AgentToolNames.GetDeal, AIFunctionFactory.Create((string dealId) => JsonDocument.Parse(mcpError).RootElement.Clone(), name: "get_deal"), false);

        //SUT
        await new GuardedToolFunction(getDeal, _context).InvokeAsync(new AIFunctionArguments { ["dealId"] = "D-1001" }, CancellationToken);

        Assert.That(_context.Deal, Is.Null);
        Assert.That(_context.ToolCalls.Single().Outcome, Is.EqualTo("error:NOT_FOUND"));
    }

    [Test]
    public async Task Invoke_EmitsToolCallSpanWithRequiredAttributes()
    {
        //SETUP
        var spans = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == O2CTelemetry.Sources.Orchestrator,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue
        };

        ActivitySource.AddActivityListener(listener);

        //SUT
        await Guard(AgentToolNames.GetDeal).InvokeAsync(new AIFunctionArguments { ["dealId"] = "D-1001" }, CancellationToken);

        var span = spans.Single(s => s.DisplayName == "tool.call crm.get_deal");

        Assert.That(span.GetTagItem(O2CTelemetry.Attributes.AgentName), Is.EqualTo(SingleOrderAgent.AgentName));
        Assert.That(span.GetTagItem(O2CTelemetry.Attributes.ToolName), Is.EqualTo(AgentToolNames.GetDeal));
        Assert.That(span.GetTagItem(O2CTelemetry.Attributes.CorrelationId), Is.EqualTo("corr-guard-test"));
        Assert.That(span.GetTagItem(O2CTelemetry.Attributes.ToolOutcome), Is.EqualTo("ok"));
        Assert.That(span.Duration, Is.GreaterThan(TimeSpan.Zero));
    }

    private GuardedToolFunction Guard(string qualifiedName) =>
        new(_tools.All().Single(t => t.QualifiedName == qualifiedName), _context);

    private static IEnumerable<string> PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    /// <summary>Codice dell'envelope di errore; <c>null</c> per un risultato positivo.</summary>
    private static string? ErrorCode(object? result) =>
        result is JsonElement { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("error", out var error)
            ? error.GetProperty("code").GetString()
            : null;
}
