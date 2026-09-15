using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Tests.Support;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Orchestrator.Workflow;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using static Dusiburg.AI.O2C.Orchestrator.Tests.Support.AgentScriptChatClient;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Workflow;

/// <summary>Workflow Intake → Fulfillment → Order con handoff (4.7): modello a copione per agente, tool e stato in memoria.</summary>
public class DealWorkflowRunnerTests
{
    private static readonly object[] Lines = [new { sku = "IND-BRG-001", quantity = 40, unitPrice = 12m }];

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task ProcessAsync_ValidDeal_HandsOffTwiceAndCreatesOrder()
    {
        //SETUP
        var tools = new FakeO2CTools();
        var store = new RecordingWorkflowStateStore();
        var chat = new AgentScriptChatClient(new()
        {
            [WorkflowAgents.Intake.Name] = Script(Call("i1", "get_deal", new() { ["dealId"] = "D-1001" }), Call("i2", "get_company", new() { ["companyId"] = "C-01" }), Handoff("i3", "Deal is valid.")),
            [WorkflowAgents.Fulfillment.Name] = Script(Call("f1", "check_stock", new() { ["sku"] = "IND-BRG-001", ["quantity"] = 40 }), Handoff("f2", "Stock checked.")),
            [WorkflowAgents.Order.Name] = Script(
                Call("o1", "get_customer", new() { ["vatNumber"] = "IT01234560157" }),
                Call("o2", "create_order", new() { ["customerId"] = 1, ["lines"] = Lines, ["idempotencyKey"] = "invented", ["externalRef"] = "D-9999" }),
                Call("o3", "update_deal", new() { ["dealId"] = "D-1001", ["status"] = "OrderCreated", ["erpOrderNumber"] = FakeO2CTools.OrderNumber, ["note"] = "ok" }))
        });
        var runner = CreateRunner(chat, tools, store);

        //SUT
        var result = await runner.ProcessAsync("D-1001", "corr-workflow-test", dealRevision: null, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(result!.ErpOrderNumber, Is.EqualTo(FakeO2CTools.OrderNumber));
        Assert.That(result.Handoffs.Select(h => (h.From, h.To)), Is.EqualTo(new[]
        {
            (WorkflowAgents.Intake.Name, WorkflowAgents.Fulfillment.Name),
            (WorkflowAgents.Fulfillment.Name, WorkflowAgents.Order.Name)
        }));
        Assert.That(result.Handoffs.Select(h => h.Reason), Is.EqualTo(new[] { "Deal is valid.", "Stock checked." }));
        Assert.That(tools.CreateOrderArguments?["idempotencyKey"], Is.EqualTo("o2c-D-1001-r3"));
        Assert.That(result.ToolCalls.Where(c => c.Tool == AgentToolNames.CreateOrder).Select(c => c.Agent), Is.EqualTo(new[] { WorkflowAgents.Order.Name }));
        Assert.That(store.Updates.Select(u => u.Phase), Is.EqualTo(new[] { WorkflowPhase.Intake, WorkflowPhase.Fulfillment, WorkflowPhase.Order, WorkflowPhase.Completed }));
        Assert.That(store.Updates[^1].StateJson, Does.Contain(FakeO2CTools.OrderNumber).And.Contain("FulfillmentAgent"));
    }

    [Test]
    public async Task ProcessAsync_EachAgentIsOfferedOnlyItsTools()
    {
        //SETUP
        var chat = new AgentScriptChatClient(new()
        {
            [WorkflowAgents.Intake.Name] = Script(Call("i1", "get_deal", new() { ["dealId"] = "D-1001" }), Handoff("i2", "ok")),
            [WorkflowAgents.Fulfillment.Name] = Script(Handoff("f1", "ok"))
        });
        var runner = CreateRunner(chat, new FakeO2CTools(), new RecordingWorkflowStateStore());

        //SUT
        await runner.ProcessAsync("D-1001", "corr-workflow-test", dealRevision: null, reprocess: true, CancellationToken);

        var offered = chat.Requests.GroupBy(r => r.Agent).ToDictionary(g => g.Key, g => g.First().Tools.Where(t => !t.StartsWith("handoff_to", StringComparison.Ordinal)).ToList());

        Assert.That(offered[WorkflowAgents.Intake.Name], Is.EquivalentTo(new[] { "get_deal", "get_company", WorkflowAgents.ReportDiscardedTool }));
        Assert.That(offered[WorkflowAgents.Fulfillment.Name], Is.EquivalentTo(new[] { "check_stock", WorkflowAgents.ReportFailedTool }));
        Assert.That(offered[WorkflowAgents.Order.Name], Is.EquivalentTo(new[] { "get_customer", "create_customer", "create_order", "update_deal" }));
    }

    [Test]
    public async Task ProcessAsync_NonEuroDeal_IsDiscardedByHostWithoutOrder()
    {
        //SETUP
        var usdDeal = FakeO2CTools.Deal with { DealId = "D-1006", Currency = "USD" };
        var tools = new FakeO2CTools(usdDeal);
        var store = new RecordingWorkflowStateStore();
        var chat = new AgentScriptChatClient(new()
        {
            [WorkflowAgents.Intake.Name] = Script(
                Call("i1", "get_deal", new() { ["dealId"] = "D-1006" }),
                Call("i2", WorkflowAgents.ReportDiscardedTool, new() { ["reason"] = "Currency is USD instead of EUR" }))
        });
        var runner = CreateRunner(chat, tools, store);

        //SUT
        var result = await runner.ProcessAsync("D-1006", "corr-workflow-test", dealRevision: null, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.Discarded));
        Assert.That(result!.Handoffs, Is.Empty);
        Assert.That(tools.UpdatedStatus, Is.EqualTo(DealStatus.Discarded));
        Assert.That(tools.UpdatedNote, Does.Contain("USD"));
        Assert.That(tools.CreateOrderCalls, Is.Zero);
        Assert.That(result.ToolCalls.Single(c => c.Tool == AgentToolNames.UpdateDeal).Agent, Is.EqualTo("Host"));
        Assert.That(store.Updates[^1].Phase, Is.EqualTo(WorkflowPhase.Discarded));
    }

    [Test]
    public async Task ProcessAsync_DiscardNotConfirmedByDealData_IsFailed()
    {
        //SETUP
        var tools = new FakeO2CTools();
        var chat = new AgentScriptChatClient(new()
        {
            [WorkflowAgents.Intake.Name] = Script(
                Call("i1", "get_deal", new() { ["dealId"] = "D-1001" }),
                Call("i2", WorkflowAgents.ReportDiscardedTool, new() { ["reason"] = "I do not like this deal" }))
        });
        var runner = CreateRunner(chat, tools, new RecordingWorkflowStateStore());

        //SUT
        var result = await runner.ProcessAsync("D-1001", "corr-workflow-test", dealRevision: null, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.Failed));
        Assert.That(result!.Reasons, Has.Some.Contains("non confermato"));
        Assert.That(tools.UpdatedStatus, Is.EqualTo(DealStatus.Failed));
    }

    [Test]
    public async Task ProcessAsync_UnknownSku_FailsAfterFulfillmentWithoutOrder()
    {
        //SETUP
        var deal = FakeO2CTools.Deal with { DealId = "D-1007", LineItems = [new DealLineItemDto("IND-SEN-999", 10, 48m)] };
        var tools = new FakeO2CTools(deal, unknownSku: "IND-SEN-999");
        var store = new RecordingWorkflowStateStore();
        var chat = new AgentScriptChatClient(new()
        {
            [WorkflowAgents.Intake.Name] = Script(Call("i1", "get_deal", new() { ["dealId"] = "D-1007" }), Handoff("i2", "ok")),
            [WorkflowAgents.Fulfillment.Name] = Script(
                Call("f1", "check_stock", new() { ["sku"] = "IND-SEN-999", ["quantity"] = 10 }),
                Call("f2", WorkflowAgents.ReportFailedTool, new() { ["reason"] = "SKU IND-SEN-999 not found" }))
        });
        var runner = CreateRunner(chat, tools, store);

        //SUT
        var result = await runner.ProcessAsync("D-1007", "corr-workflow-test", dealRevision: null, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.Failed));
        Assert.That(result!.Handoffs.Select(h => h.To), Is.EqualTo(new[] { WorkflowAgents.Fulfillment.Name }));
        Assert.That(result.ToolCalls.Single(c => c.Tool == AgentToolNames.CheckStock).Outcome, Is.EqualTo("error:NOT_FOUND"));
        Assert.That(tools.UpdatedStatus, Is.EqualTo(DealStatus.Failed));
        Assert.That(tools.CreateOrderCalls, Is.Zero);
        Assert.That(store.Updates[^1].Phase, Is.EqualTo(WorkflowPhase.Failed));
    }

    [Test]
    public async Task ProcessAsync_EventAlreadyReceived_StartsNoWorkflow()
    {
        //SETUP
        var chat = new AgentScriptChatClient([]);
        var store = new RecordingWorkflowStateStore { AlreadyReceived = true };
        var runner = CreateRunner(chat, new FakeO2CTools(), store);

        //SUT
        var result = await runner.ProcessAsync("D-1001", "corr-workflow-test", dealRevision: 3, reprocess: false, CancellationToken);

        Assert.That(result, Is.Null);
        Assert.That(store.StartCalls, Is.EqualTo(1));
        Assert.That(chat.Requests, Is.Empty);
        Assert.That(store.Updates, Is.Empty);
    }

    [TestCase("ClosedWon", "EUR", 480, false)]
    [TestCase("ClosedWon", "USD", 480, true)]
    [TestCase("ContractSent", "EUR", 480, true)]
    [TestCase("ClosedWon", "EUR", 999, true)]
    public void IntakeRejects_DealData_MatchesIntakeRules(string stage, string currency, decimal amount, bool rejected)
    {
        //SETUP
        var deal = FakeO2CTools.Deal with { Stage = stage, Currency = currency, Amount = amount };

        //SUT
        Assert.That(DealWorkflowRunner.IntakeRejects(deal), Is.EqualTo(rejected));
    }

    [Test]
    public void NextOf_OnlyForwardEdges()
    {
        //SUT
        Assert.That(
            WorkflowAgents.All.Select(a => (a.Name, WorkflowAgents.NextOf(a.Name)?.Name)),
            Is.EqualTo(new (string, string?)[]
            {
                (WorkflowAgents.Intake.Name, WorkflowAgents.Fulfillment.Name),
                (WorkflowAgents.Fulfillment.Name, WorkflowAgents.Order.Name),
                (WorkflowAgents.Order.Name, null)
            }));
    }

    private static DealWorkflowRunner CreateRunner(IChatClient chat, FakeO2CTools tools, RecordingWorkflowStateStore store) =>
        new(new ScriptedModelClientFactory(chat), new InMemoryToolCatalog(tools.All()), store, NullLogger<DealWorkflowRunner>.Instance);

    private static ChatResponse Call(string callId, string toolName, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, toolName, arguments)]));

    private static ChatResponse Handoff(string callId, string reason) =>
        Call(callId, "handoff_to_1", new() { ["reasonForHandoff"] = reason });
}
