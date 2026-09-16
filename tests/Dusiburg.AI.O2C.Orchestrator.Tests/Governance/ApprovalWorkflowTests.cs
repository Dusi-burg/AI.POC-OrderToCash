using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Tests.Support;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Orchestrator.Workflow;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.Extensions.AI;
using static Dusiburg.AI.O2C.Orchestrator.Tests.Support.AgentScriptChatClient;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Governance;

/// <summary>
/// Sospensione su <c>create_order</c>, ripresa dopo la decisione, rifiuto e scadenza (5.9). Il modello è a copione e i
/// tool sono in memoria: quello che si verifica è il comportamento dell'host, non la bravura del modello.
/// </summary>
public class ApprovalWorkflowTests
{
    /// <summary>Soglia bassa: il deal demo da 480 € la supera, così lo scenario "sopra soglia" non richiede altri dati.</summary>
    private const decimal LowThreshold = 100m;

    /// <summary>Tempi della sweep usati dai test: scadenza immediata, intervallo qualunque.</summary>
    private static readonly ApprovalSweepOptions DemoSweep = new(TimeSpan.Zero, TimeSpan.FromMinutes(5));

    private static readonly object[] Lines = [new { sku = "IND-BRG-001", quantity = 40, unitPrice = 12m }];

    private CancellationTokenSource _timeout = null!;

    /// <summary>Guardia sui tempi: un workflow che non si ferma farebbe fallire il test invece di bloccare la suite.</summary>
    private CancellationToken CancellationToken => _timeout.Token;

    private FakeO2CTools _tools = null!;
    private RecordingWorkflowStateStore _states = null!;
    private RecordingApprovalStore _approvals = null!;
    private AgentScriptChatClient _chat = null!;
    private string _correlationId = null!;

    [SetUp]
    public void Setup()
    {
        // Una istanza per classe di test: lo stato va rifatto a ogni test (D28).
        _tools = new FakeO2CTools();
        _states = new RecordingWorkflowStateStore();
        _approvals = new RecordingApprovalStore(_states);
        _chat = OrderScript();
        _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Sessione di checkpoint distinta per test: il manager in memoria è condiviso dalla classe.
        _correlationId = $"corr-approval-{Guid.NewGuid():N}";
    }

    [TearDown]
    public void Teardown()
    {
        _chat.Dispose();
        _timeout.Dispose();
    }

    [Test]
    public async Task ProcessAsync_PolicyRequiresApproval_SuspendsWithoutCallingErp()
    {
        //SUT
        var result = await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.ApprovalPending));
        Assert.That(_tools.CreateOrderCalls, Is.Zero, "create_order non deve raggiungere l'ERP prima dell'approvazione");

        var approval = _approvals.Created.Single();
        Assert.That(approval.Reasons, Is.EqualTo(new[] { ApprovalReason.OverThreshold }));
        Assert.That(approval.Payload.Total, Is.EqualTo(480m));
        Assert.That(approval.Payload.Lines.Select(l => l.Sku), Is.EqualTo(new[] { "IND-BRG-001" }));
        Assert.That(approval.Payload.IdempotencyKey, Is.EqualTo("o2c-D-1001-r3"));
        Assert.That(approval.CheckpointId, Is.Not.Null, "senza checkpoint il workflow non potrebbe riprendere");

        Assert.That(_states.Updates[^1].Phase, Is.EqualTo(WorkflowPhase.AwaitingApproval));
        Assert.That(_tools.UpdatedStatus, Is.EqualTo(DealStatus.ApprovalPending));
        Assert.That(_tools.UpdatedNote, Does.Contain("OverThreshold"));
    }

    [Test]
    public async Task ResumeAsync_Approved_CreatesTheOrderOnceEvenWithDuplicateDeliveries()
    {
        //SETUP
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;
        _approvals.Decide(approvalId, ApprovalStatus.Approved, note: "Va bene.");

        //SUT: la prima consegna riprende il workflow, la seconda e la sweep non devono rifare nulla.
        var first = await Resumer().ResumeAsync(approvalId, CancellationToken);
        var second = await Resumer().ResumeAsync(approvalId, CancellationToken);
        await Sweep().ReconcileAsync(DemoSweep, CancellationToken);

        Assert.That(first, Is.EqualTo(ApprovalResumeOutcome.Completed));
        Assert.That(second, Is.EqualTo(ApprovalResumeOutcome.AlreadyResumed));
        Assert.That(_tools.CreateOrderCalls, Is.EqualTo(1));
        Assert.That(_tools.CreateOrderArguments?["idempotencyKey"], Is.EqualTo("o2c-D-1001-r3"));
        Assert.That(_tools.UpdatedStatuses, Is.EqualTo(new[] { DealStatus.ApprovalPending, DealStatus.OrderCreated }));
        Assert.That(_approvals.PhaseOf(_correlationId), Is.EqualTo(WorkflowPhase.Completed));
    }

    [Test]
    public async Task ResumeAsync_TwoDeliveriesAtTheSameTime_ResumeOnlyOnce()
    {
        //SETUP: al riavvio partono insieme il consumer di approval-decided e la sweep di riconciliazione.
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;
        _approvals.Decide(approvalId, ApprovalStatus.Approved);

        //SUT
        var outcomes = await Task.WhenAll(
            Resumer().ResumeAsync(approvalId, CancellationToken),
            Resumer().ResumeAsync(approvalId, CancellationToken));

        Assert.That(_approvals.Claims, Is.EqualTo(1), "solo una delle due consegne deve prendere possesso della ripresa");
        Assert.That(outcomes, Does.Contain(ApprovalResumeOutcome.AlreadyResumed));
        Assert.That(outcomes, Does.Contain(ApprovalResumeOutcome.Completed));

        // Gli effetti collaterali non idempotenti — le note sul CRM — devono restare singoli.
        Assert.That(_tools.CreateOrderCalls, Is.EqualTo(1));
        Assert.That(_tools.UpdatedStatuses, Is.EqualTo(new[] { DealStatus.ApprovalPending, DealStatus.OrderCreated }));
    }

    [Test]
    public async Task ResumeAsync_ApprovedOnAnotherInstance_CompletesTheWorkflow()
    {
        //SETUP: host A crea la richiesta e si ferma; il database (stati, richieste e checkpoint) è l'unica cosa che resta.
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;
        _approvals.Decide(approvalId, ApprovalStatus.Approved);

        // Host B: agenti, tool e contesto ricostruiti da zero, come in un processo appena avviato.
        var resumer = WorkflowTestHost.CreateResumer(_chat, _tools, _states, _approvals, LowThreshold);

        //SUT
        var outcome = await resumer.ResumeAsync(approvalId, CancellationToken);

        Assert.That(outcome, Is.EqualTo(ApprovalResumeOutcome.Completed));
        Assert.That(_tools.CreateOrderCalls, Is.EqualTo(1));
        Assert.That(_tools.UpdatedStatus, Is.EqualTo(DealStatus.OrderCreated));
    }

    [Test]
    public async Task ResumeAsync_Rejected_CreatesNoOrderAndMarksTheDeal()
    {
        //SETUP
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;
        _approvals.Decide(approvalId, ApprovalStatus.Rejected, decidedBy: "capo@dusiburg.local", note: "Margine troppo basso.");

        //SUT
        var outcome = await Resumer().ResumeAsync(approvalId, CancellationToken);

        Assert.That(outcome, Is.EqualTo(ApprovalResumeOutcome.Closed));
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
        Assert.That(_tools.UpdatedStatus, Is.EqualTo(DealStatus.Rejected));
        Assert.That(_tools.UpdatedNote, Does.Contain("capo@dusiburg.local").And.Contain("Margine troppo basso."));
        Assert.That(_approvals.PhaseOf(_correlationId), Is.EqualTo(WorkflowPhase.Completed));
    }

    [Test]
    public async Task ExpireAsync_PendingBeyondTimeout_MarksExpiredWithoutOrder()
    {
        //SETUP
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;

        //SUT: timeout nullo, quindi la richiesta appena creata è già scaduta.
        await Sweep().ExpireAsync(DemoSweep, CancellationToken);

        var approval = await _approvals.FindAsync(approvalId, CancellationToken);

        Assert.That(approval!.Status, Is.EqualTo(ApprovalStatus.Expired));
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
        Assert.That(_tools.UpdatedStatus, Is.EqualTo(DealStatus.Expired));
        Assert.That(_approvals.PhaseOf(_correlationId), Is.EqualTo(WorkflowPhase.Completed));
    }

    [Test]
    public async Task ExpireAsync_AlreadyDecided_DoesNotExpire()
    {
        //SETUP
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;
        _approvals.Decide(approvalId, ApprovalStatus.Approved);

        //SUT
        await Sweep().ExpireAsync(DemoSweep, CancellationToken);

        var approval = await _approvals.FindAsync(approvalId, CancellationToken);

        Assert.That(approval!.Status, Is.EqualTo(ApprovalStatus.Approved));
    }

    [Test]
    public async Task ResumeAsync_StillPending_DoesNothing()
    {
        //SETUP
        await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);

        //SUT
        var outcome = await Resumer().ResumeAsync(_approvals.Created.Single().ApprovalId, CancellationToken);

        Assert.That(outcome, Is.EqualTo(ApprovalResumeOutcome.StillPending));
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
    }

    [Test]
    public async Task ResumeAsync_UnknownApproval_IsNotFound()
    {
        //SUT
        var outcome = await Resumer().ResumeAsync(Guid.CreateVersion7(), CancellationToken);

        Assert.That(outcome, Is.EqualTo(ApprovalResumeOutcome.NotFound));
    }

    [Test]
    public async Task ProcessAsync_BlockedCustomer_SuspendsWithThatReason()
    {
        //SETUP: sotto soglia, giacenza disponibile, cliente esistente ma bloccato (D-1005).
        _tools = new FakeO2CTools(customer: FakeO2CTools.Customer with { IsBlocked = true });

        //SUT
        var result = await Runner().ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.ApprovalPending));
        Assert.That(_approvals.Created.Single().Reasons, Does.Contain(ApprovalReason.BlockedCustomer));
        Assert.That(_approvals.Created.Single().Payload.CustomerIsBlocked, Is.True);
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
    }

    [Test]
    public async Task ProcessAsync_NewCustomerAndBackorder_SuspendsWithBothReasons()
    {
        //SETUP: cliente assente in ERP (D-1004) e riga senza giacenza (D-1003).
        _tools = new FakeO2CTools(unavailableSku: "IND-BRG-001", customerExists: false);
        _chat = OrderScript(createCustomer: true);

        //SUT
        var result = await Runner(threshold: 10_000m).ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.ApprovalPending));

        var approval = _approvals.Created.Single();

        Assert.That(approval.Reasons, Is.EqualTo(new[] { ApprovalReason.InsufficientStock, ApprovalReason.NewCustomer }));
        Assert.That(approval.Payload.CustomerCreatedInThisRun, Is.True);
        Assert.That(approval.Payload.Lines.Single().Available, Is.False);
        Assert.That(_tools.CreateCustomerCalls, Is.EqualTo(1), "l'anagrafica creata prima della sospensione resta in ERP (G5.2)");
        Assert.That(_tools.CreateOrderCalls, Is.Zero);
    }

    [Test]
    public async Task ProcessAsync_FulfillmentAgentSkippedTheStockCheck_HostVerifiesAndSuspends()
    {
        //SETUP: il copione di FulfillmentAgent passa la mano senza chiamare check_stock, come ha fatto qwen su D-1003.
        _tools = new FakeO2CTools(unavailableSku: "IND-BRG-001");
        _chat = OrderScript(skipStockCheck: true);

        //SUT: sotto soglia, cliente esistente e non bloccato: l'unico motivo possibile è la giacenza.
        var result = await Runner(threshold: 10_000m).ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);

        Assert.That(result?.Status, Is.EqualTo(DealStatus.ApprovalPending), "la verifica mancante non deve far passare l'ordine");
        Assert.That(_approvals.Created.Single().Reasons, Is.EqualTo(new[] { ApprovalReason.InsufficientStock }));
        Assert.That(_tools.CreateOrderCalls, Is.Zero);

        // La verifica su cui la policy ha deciso è quella dell'host, fatta sulle righe proposte.
        Assert.That(result!.ToolCalls.Where(c => c.Tool == AgentToolNames.CheckStock).Select(c => c.Agent), Is.EqualTo(new[] { "Host" }));
        Assert.That(_approvals.Created.Single().Payload.Lines.Single().Available, Is.False);
    }

    [Test]
    public async Task ResumeAsync_BackorderedOrder_ReportsWhatIsMissingOnTheDeal()
    {
        //SETUP
        _tools = new FakeO2CTools(unavailableSku: "IND-BRG-001");
        await Runner(threshold: 10_000m).ProcessAsync("D-1001", _correlationId, dealRevision: 3, reprocess: true, CancellationToken);
        var approvalId = _approvals.Created.Single().ApprovalId;
        _approvals.Decide(approvalId, ApprovalStatus.Approved, note: "Procediamo comunque.");

        //SUT
        var outcome = await Resumer(threshold: 10_000m).ResumeAsync(approvalId, CancellationToken);

        Assert.That(outcome, Is.EqualTo(ApprovalResumeOutcome.Completed));
        Assert.That(_tools.CreateOrderCalls, Is.EqualTo(1));
        Assert.That(_tools.UpdatedStatuses, Is.EqualTo(new[] { DealStatus.ApprovalPending, DealStatus.OrderCreated, DealStatus.OrderCreated }));
        Assert.That(_tools.UpdatedNote, Does.Contain("IND-BRG-001: 40 PZ da ordinare"), "il deal CRM deve dire cosa manca");
    }

    private DealWorkflowRunner Runner(decimal threshold = LowThreshold) =>
        WorkflowTestHost.CreateRunner(_chat, _tools, _states, _approvals, threshold);

    private ApprovalResumeRunner Resumer(decimal threshold = LowThreshold) =>
        WorkflowTestHost.CreateResumer(_chat, _tools, _states, _approvals, threshold);

    private ApprovalSweepService Sweep() => WorkflowTestHost.CreateSweep(_approvals, Resumer());

    /// <summary>Copione completo del percorso felice: l'ultimo passo di OrderAgent viene usato solo dopo l'approvazione.</summary>
    /// <summary>
    /// Copione completo del percorso felice. Con <paramref name="skipStockCheck"/> vero, FulfillmentAgent passa la mano
    /// senza verificare la giacenza: è il comportamento osservato dal vivo su D-1003.
    /// </summary>
    private static AgentScriptChatClient OrderScript(bool createCustomer = false, bool skipStockCheck = false)
    {
        var order = createCustomer
            ? Script(
                Call("o1", "get_customer", new() { ["vatNumber"] = "IT01234560157" }),
                Call("o1b", "create_customer", new() { ["name"] = "Nuova Robotica Marche S.r.l.", ["vatNumber"] = "IT11234560422", ["email"] = "acquisti@nuovaroboticamarche.it", ["address"] = "Via dell'Artigianato 9" }),
                CreateOrder(),
                UpdateDeal())
            : Script(
                Call("o1", "get_customer", new() { ["vatNumber"] = "IT01234560157" }),
                CreateOrder(),
                UpdateDeal());

        return new AgentScriptChatClient(new()
        {
            [WorkflowAgents.Intake.Name] = Script(
                Call("i1", "get_deal", new() { ["dealId"] = "D-1001" }),
                Call("i2", "get_company", new() { ["companyId"] = "C-01" }),
                Handoff("i3", "Deal is valid.")),
            [WorkflowAgents.Fulfillment.Name] = skipStockCheck
                ? Script(Handoff("f1", "Deal validated and ready for stock check"))
                : Script(
                    Call("f1", "check_stock", new() { ["sku"] = "IND-BRG-001", ["quantity"] = 40 }),
                    Handoff("f2", "Stock checked.")),
            [WorkflowAgents.Order.Name] = order
        });
    }

    private static ChatResponse CreateOrder() =>
        Call("o2", "create_order", new() { ["customerId"] = 1, ["lines"] = Lines });

    private static ChatResponse UpdateDeal() =>
        Call("o3", "update_deal", new()
        {
            ["dealId"] = "D-1001",
            ["status"] = "OrderCreated",
            ["erpOrderNumber"] = FakeO2CTools.OrderNumber,
            ["note"] = "Ordine creato dopo approvazione."
        });

    private static ChatResponse Call(string callId, string toolName, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, toolName, arguments)]));

    private static ChatResponse Handoff(string callId, string reason) =>
        Call(callId, "handoff_to_1", new() { ["reasonForHandoff"] = reason });
}
