using System.Collections.Frozen;
using System.ComponentModel;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>Un agente del workflow (4.1): istruzioni, tool MCP consentiti e verdetto di arresto disponibile.</summary>
public sealed record AgentDefinition(string Name, string Description, string Instructions, IReadOnlySet<string> AllowedTools, DealStatus? StopVerdict);

/// <summary>I tre agenti di §5 e la loro topologia a soli archi in avanti: Intake → Fulfillment → Order.</summary>
public static class WorkflowAgents
{
    public const string ReportDiscardedTool = "report_discarded";

    public const string ReportFailedTool = "report_failed";

    /// <summary>
    /// Descrizioni dei tool di handoff: il modello le vede dal primo turno, quindi sono scritte come condizioni d'uso e non
    /// come fatti già avvenuti ("Stock was checked…" faceva passare la mano a FulfillmentAgent senza verificare, D62).
    /// </summary>
    public const string IntakeHandoffCondition =
        "Use only after get_deal and get_company were called and the deal satisfies every validation rule.";

    public const string FulfillmentHandoffCondition =
        "Use only after check_stock was called for every line item and none returned NOT_FOUND.";

    public static AgentDefinition Intake { get; } = new(
        "IntakeAgent",
        "Validates the CRM deal before any ERP work.",
        """
        You are IntakeAgent in an order-to-cash workflow. Validate the CRM deal named by the user.
        1. Call get_deal with the deal id, then get_company with its companyId.
        2. The deal is valid when: stage is ClosedWon, currency is EUR, it has at least one line item,
           and amount equals the sum of quantity × unitPrice of the line items.
        3. If the deal is valid, hand off to FulfillmentAgent.
           If it is not valid, call report_discarded with a short reason and stop.
        Never invent data: use only values returned by the tools.
        """,
        new[] { AgentToolNames.GetDeal, AgentToolNames.GetCompany }.ToFrozenSet(),
        DealStatus.Discarded);

    public static AgentDefinition Fulfillment { get; } = new(
        "FulfillmentAgent",
        "Checks ERP stock for every line of the validated deal.",
        """
        You are FulfillmentAgent in an order-to-cash workflow. The deal validated by IntakeAgent is in the conversation.
        Stock has NOT been checked yet: checking it is your job.
        1. Call check_stock once for every line item of the deal, with its sku and quantity.
        2. A line with available = false is acceptable: the order will be created in backorder.
        3. If check_stock returns NOT_FOUND for a SKU, call report_failed with a short reason that names the SKU and stop.
           Otherwise hand off to OrderAgent.
        Never hand off before you have called check_stock for every line item of the deal.
        Only hand off when no check_stock call returned NOT_FOUND.
        Never invent data: use only values returned by the tools.
        """,
        new[] { AgentToolNames.CheckStock }.ToFrozenSet(),
        DealStatus.Failed);

    public static AgentDefinition Order { get; } = new(
        "OrderAgent",
        "Resolves the ERP customer, creates the order and updates the CRM deal.",
        """
        You are OrderAgent in an order-to-cash workflow. Use the deal and company found earlier in the conversation.
        Take the order lines only from the get_deal result: stock availability does not change the quantities to order.
        1. Call get_customer with the vatNumber of the company. If customer is null, call get_customer with the email of the company.
           If it is still null, call create_customer with name, vatNumber, email and address of the company.
        2. Call create_order with the customerId and the line items of the deal (sku, quantity and unitPrice exactly as in the deal).
        3. Call update_deal with the deal id, status OrderCreated, the orderNumber returned by create_order as erpOrderNumber, and a short note.
        4. Reply with a one-line summary.
        Never invent ids, numbers or prices: use only values returned by the tools.
        """,
        new[] { AgentToolNames.GetCustomer, AgentToolNames.CreateCustomer, AgentToolNames.CreateOrder, AgentToolNames.UpdateDeal }.ToFrozenSet(),
        null);

    public static IReadOnlyList<AgentDefinition> All { get; } = [Intake, Fulfillment, Order];

    public static IReadOnlySet<string> Names { get; } = All.Select(a => a.Name).ToFrozenSet();

    /// <summary>Unico successore di ogni agente (nessun arco di ritorno).</summary>
    public static AgentDefinition? NextOf(string agentName) =>
        agentName == Intake.Name ? Fulfillment : agentName == Fulfillment.Name ? Order : null;

    /// <summary>Tool locale dell'host che registra il verdetto di arresto nel contesto (non è un tool MCP).</summary>
    public static AIFunction CreateVerdictTool(AgentDefinition agent, DealRunContext context)
    {
        var status = agent.StopVerdict ?? throw new InvalidOperationException($"{agent.Name} non ha un verdetto di arresto.");
        var name = status == DealStatus.Discarded ? ReportDiscardedTool : ReportFailedTool;

        return AIFunctionFactory.Create(
            ([Description("Short reason for stopping the workflow.")] string reason) =>
            {
                context.RecordVerdict(new AgentVerdict(agent.Name, status, reason));

                return $"Recorded: the deal will be marked {status}. Stop now.";
            },
            name,
            status == DealStatus.Discarded
                ? "Stops the workflow because the deal is not valid (it will be marked Discarded)."
                : "Stops the workflow because the order cannot be fulfilled (it will be marked Failed).");
    }
}
