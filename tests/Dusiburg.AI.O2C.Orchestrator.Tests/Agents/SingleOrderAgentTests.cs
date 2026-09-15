using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Tests.Support;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using static Dusiburg.AI.O2C.Orchestrator.Tests.Support.ScriptedChatClient;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Agents;

/// <summary>Agente singolo con modello a copione e tool in memoria (3.8, 3.9).</summary>
public class SingleOrderAgentTests
{
    private const string ValidOutcome = """{"dealId":"D-1001","status":"OrderCreated","erpOrderNumber":"SO-2026-000001","reasons":[],"note":"Order created."}""";

    private const string WorkSummary = "Order SO-2026-000001 created and deal updated.";

    private static readonly object[] Lines = [new { sku = "IND-BRG-001", quantity = 40, unitPrice = 12m }];

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task ProcessAsync_HappyPath_ReturnsOrderCreatedFromToolFacts()
    {
        //SETUP
        var tools = new FakeO2CTools();
        var chat = new ScriptedChatClient([.. HappyPathToolCalls(), _ => Text(WorkSummary), _ => Text(ValidOutcome)]);
        var agent = CreateAgent(chat, tools);

        //SUT
        var result = await agent.ProcessAsync("D-1001", "corr-agent-test", CancellationToken);

        Assert.That(result.Status, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(result.ErpOrderNumber, Is.EqualTo(FakeO2CTools.OrderNumber));
        Assert.That(chat.Options[0]?.ResponseFormat, Is.Null.Or.InstanceOf<ChatResponseFormatText>());
        Assert.That(chat.Options[^1]?.ResponseFormat, Is.InstanceOf<ChatResponseFormatJson>());
        Assert.That(result.ModelOutcomeValid, Is.True);
        Assert.That(result.Reasons, Is.Empty);
        Assert.That(tools.CreateOrderArguments?["idempotencyKey"], Is.EqualTo("o2c-D-1001-r3"));
        Assert.That(tools.UpdatedStatus, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(result.ToolCalls.Select(c => c.Tool), Is.EqualTo(new[]
        {
            AgentToolNames.GetDeal, AgentToolNames.GetCompany, AgentToolNames.CheckStock, AgentToolNames.GetCustomer,
            AgentToolNames.CreateOrder, AgentToolNames.UpdateDeal
        }));
    }

    [Test]
    public async Task ProcessAsync_OffersOnlyAllowedToolsWithHiddenInjectedArguments()
    {
        //SETUP
        var chat = new ScriptedChatClient([.. HappyPathToolCalls(), _ => Text(WorkSummary), _ => Text(ValidOutcome)]);
        var agent = CreateAgent(chat, new FakeO2CTools());

        //SUT
        await agent.ProcessAsync("D-1001", "corr-agent-test", CancellationToken);

        var offered = chat.Options[0]!.Tools!.OfType<AIFunction>().ToList();
        var createOrderSchema = offered.Single(t => t.Name == "create_order").JsonSchema.GetRawText();

        Assert.That(offered.Select(t => t.Name), Does.Not.Contain("get_order"));
        Assert.That(offered, Has.Count.EqualTo(SingleOrderAgent.AllowedTools.Count));
        Assert.That(createOrderSchema, Does.Not.Contain("idempotencyKey").And.Not.Contain("externalRef"));
    }

    [Test]
    public async Task ProcessAsync_InvalidOutcome_AsksAgainOnceWithoutRepeatingTools()
    {
        //SETUP
        var tools = new FakeO2CTools();
        var chat = new ScriptedChatClient([.. HappyPathToolCalls(), _ => Text(WorkSummary), _ => Text("Done, the order was created."), _ => Text(ValidOutcome)]);
        var agent = CreateAgent(chat, tools);

        //SUT
        var result = await agent.ProcessAsync("D-1001", "corr-agent-test", CancellationToken);

        Assert.That(result.Status, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(result.ModelOutcomeValid, Is.True);
        Assert.That(tools.CreateOrderCalls, Is.EqualTo(1));
        Assert.That(chat.RequestCount, Is.EqualTo(9));
    }

    [Test]
    public async Task ProcessAsync_OutcomeNeverValid_KeepsStatusFromToolFacts()
    {
        //SETUP
        var chat = new ScriptedChatClient([.. HappyPathToolCalls(), _ => Text(WorkSummary), _ => Text("not json"), _ => Text("still not json")]);
        var agent = CreateAgent(chat, new FakeO2CTools());

        //SUT
        var result = await agent.ProcessAsync("D-1001", "corr-agent-test", CancellationToken);

        Assert.That(result.Status, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(result.ModelOutcomeValid, Is.False);
        Assert.That(result.Reasons, Has.Some.Contains("esito valido"));
    }

    [Test]
    public async Task ProcessAsync_ModelClaimsSuccessWithoutTools_ReturnsFailed()
    {
        //SETUP
        var tools = new FakeO2CTools();
        var chat = new ScriptedChatClient(_ => Text("I created the order."), _ => Text(ValidOutcome));
        var agent = CreateAgent(chat, tools);

        //SUT
        var result = await agent.ProcessAsync("D-1001", "corr-agent-test", CancellationToken);

        Assert.That(result.Status, Is.EqualTo(DealStatus.Failed));
        Assert.That(result.ErpOrderNumber, Is.Null);
        Assert.That(result.Reasons, Has.Some.Contains("prevalgono i tool"));
        Assert.That(tools.CreateOrderCalls, Is.Zero);
    }

    private static SingleOrderAgent CreateAgent(ScriptedChatClient chat, FakeO2CTools tools) =>
        new(new ScriptedModelClientFactory(chat), new InMemoryToolCatalog(tools.All()), NullLogger<SingleOrderAgent>.Instance);

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse>[] HappyPathToolCalls() =>
    [
        _ => Call("1", "get_deal", new() { ["dealId"] = "D-1001" }),
        _ => Call("2", "get_company", new() { ["companyId"] = "C-01" }),
        _ => Call("3", "check_stock", new() { ["sku"] = "IND-BRG-001", ["quantity"] = 40 }),
        _ => Call("4", "get_customer", new() { ["vatNumber"] = "IT01234560157" }),
        _ => Call("5", "create_order", new() { ["customerId"] = 1, ["lines"] = Lines, ["idempotencyKey"] = "invented", ["externalRef"] = "D-9999" }),
        _ => Call("6", "update_deal", new() { ["dealId"] = "D-1001", ["status"] = "OrderCreated", ["erpOrderNumber"] = FakeO2CTools.OrderNumber, ["note"] = "ok" }),
    ];
}
