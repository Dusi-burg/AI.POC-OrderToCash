using Dusiburg.AI.O2C.Orchestrator.Governance;
using Dusiburg.AI.O2C.Orchestrator.Tests.Support;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Governance;

/// <summary>
/// Regole di approvazione di §7 (5.9): ogni regola da sola, le combinazioni, il confine esatto della soglia e la
/// soglia presa dalla configurazione. È la fonte unica delle regole, quindi va coperta caso per caso.
/// </summary>
public class ApprovalPolicyTests
{
    private static readonly CustomerDto KnownCustomer = FakeO2CTools.Customer;

    private static readonly CustomerDto BlockedCustomer = KnownCustomer with { IsBlocked = true };

    [Test]
    public void Evaluate_EverythingInOrder_RequiresNoApproval()
    {
        //SETUP
        var context = Context(Lines(10, 100m), Stock(("IND-BRG-001", true)));

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Required, Is.False);
        Assert.That(decision.Reasons, Is.Empty);
    }

    [Test]
    public void Evaluate_TotalAboveThreshold_RequiresApproval()
    {
        //SETUP
        var context = Context(Lines(100, 100.01m), Stock(("IND-BRG-001", true)));

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Required, Is.True);
        Assert.That(decision.Reasons, Is.EqualTo(new[] { ApprovalReason.OverThreshold }));
    }

    [Test]
    public void Evaluate_TotalExactlyOnThreshold_RequiresNoApproval()
    {
        //SETUP: il confronto è stretto, quindi 10.000 € esatti passano senza approvazione.
        var context = Context(Lines(100, 100m), Stock(("IND-BRG-001", true)));

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(context.Total, Is.EqualTo(ApprovalPolicy.DefaultThresholdEur));
        Assert.That(decision.Required, Is.False);
    }

    [Test]
    public void Evaluate_ThresholdFromConfiguration_IsUsed()
    {
        //SETUP
        var context = Context(Lines(10, 100m), Stock(("IND-BRG-001", true)));

        //SUT
        var decision = WorkflowTestHost.Policy(thresholdEur: 500m).Evaluate(context);

        Assert.That(WorkflowTestHost.Policy(thresholdEur: 500m).ThresholdEur, Is.EqualTo(500m));
        Assert.That(decision.Reasons, Is.EqualTo(new[] { ApprovalReason.OverThreshold }));
    }

    [Test]
    public void Evaluate_LineWithoutStock_RequiresApproval()
    {
        //SETUP
        var context = Context(Lines(10, 100m), Stock(("IND-BRG-001", false)));

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Reasons, Is.EqualTo(new[] { ApprovalReason.InsufficientStock }));
    }

    [Test]
    public void Evaluate_LineWithoutStockCheck_RequiresNoApproval()
    {
        //SETUP: una riga senza verifica non è una prova di indisponibilità.
        var context = Context(Lines(10, 100m), []);

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Required, Is.False);
    }

    [Test]
    public void Evaluate_CustomerCreatedInThisRun_RequiresApproval()
    {
        //SETUP
        var context = Context(Lines(10, 100m), Stock(("IND-BRG-001", true)), customerCreated: true);

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Reasons, Is.EqualTo(new[] { ApprovalReason.NewCustomer }));
    }

    [Test]
    public void Evaluate_BlockedCustomer_RequiresApproval()
    {
        //SETUP
        var context = Context(Lines(10, 100m), Stock(("IND-BRG-001", true)), customer: BlockedCustomer);

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Reasons, Is.EqualTo(new[] { ApprovalReason.BlockedCustomer }));
    }

    [Test]
    public void Evaluate_SeveralRulesTogether_ReportsThemAll()
    {
        //SETUP: come D-1008, sopra soglia e con cliente nuovo, qui anche senza giacenza e bloccato.
        var context = Context(
            Lines(100, 200m),
            Stock(("IND-BRG-001", false)),
            customer: BlockedCustomer,
            customerCreated: true);

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(decision.Required, Is.True);
        Assert.That(decision.Reasons, Is.EqualTo(new[]
        {
            ApprovalReason.OverThreshold,
            ApprovalReason.InsufficientStock,
            ApprovalReason.NewCustomer,
            ApprovalReason.BlockedCustomer
        }));
    }

    [Test]
    public void Evaluate_TotalComesFromLines_NotFromTheModel()
    {
        //SETUP: due righe che da sole stanno sotto soglia ma insieme la superano.
        var context = Context(
            [new OrderLineInput("IND-BRG-001", 60, 100m), new OrderLineInput("IND-MOT-001", 50, 100m)],
            Stock(("IND-BRG-001", true), ("IND-MOT-001", true)));

        //SUT
        var decision = WorkflowTestHost.Policy().Evaluate(context);

        Assert.That(context.Total, Is.EqualTo(11_000m));
        Assert.That(decision.Reasons, Is.EqualTo(new[] { ApprovalReason.OverThreshold }));
    }

    private static ApprovalContext Context(
        IReadOnlyList<OrderLineInput> lines,
        IReadOnlyList<StockCheckDto> stock,
        CustomerDto? customer = null,
        bool customerCreated = false) =>
        new(lines, stock, customer ?? KnownCustomer, customerCreated);

    private static IReadOnlyList<OrderLineInput> Lines(int quantity, decimal unitPrice) =>
        [new OrderLineInput("IND-BRG-001", quantity, unitPrice)];

    private static IReadOnlyList<StockCheckDto> Stock(params (string Sku, bool Available)[] checks) =>
        [.. checks.Select(c => new StockCheckDto(c.Sku, c.Available, c.Available ? 500 : 0, 3))];
}
