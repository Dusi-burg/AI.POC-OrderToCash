using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>Tool ERP e CRM in memoria con le stesse firme dei server MCP; registrano gli argomenti ricevuti.</summary>
internal sealed class FakeO2CTools
{
    public const string OrderNumber = "SO-2026-000001";

    public static readonly DealDto Deal = new("D-1001", 3, "Ricambi cuscinetti linea 2", 480m, "EUR", "ClosedWon", "C-01",
        [new DealLineItemDto("IND-BRG-001", 40, 12m)]);

    public Dictionary<string, object?>? CreateOrderArguments { get; private set; }

    public int CreateOrderCalls { get; private set; }

    public DealStatus? UpdatedStatus { get; private set; }

    public AgentTool[] All() =>
    [
        Tool(AgentToolNames.GetDeal, (string dealId) => Deal),
        Tool(AgentToolNames.GetCompany, (string companyId) =>
            new CompanyDto(companyId, "Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "acquisti@brambilla-om.it", "Via dell'Industria 12")),
        Tool(AgentToolNames.CheckStock, (string sku, int quantity) => new StockCheckDto(sku, true, 500, 3)),
        Tool(AgentToolNames.GetCustomer, (string? vatNumber, string? email) =>
            new GetCustomerResponse(new CustomerDto(1, "Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "acquisti@brambilla-om.it", 50_000m, false))),
        Tool(AgentToolNames.CreateCustomer, (string name, string vatNumber, string email, string address) => new CreateCustomerResponse(99)),
        Tool(AgentToolNames.CreateOrder, (int customerId, List<OrderLineInput> lines, string externalRef, string idempotencyKey) =>
        {
            CreateOrderCalls++;
            CreateOrderArguments = new() { ["customerId"] = customerId, ["externalRef"] = externalRef, ["idempotencyKey"] = idempotencyKey, ["lines"] = lines };

            return new CreateOrderResponse(Guid.Parse("0199a000-0000-7000-8000-000000000001"), OrderNumber, 480m, OrderStatus.Confirmed);
        }),
        Tool(AgentToolNames.UpdateDeal, (string dealId, DealStatus status, string? erpOrderNumber, string? note) =>
        {
            UpdatedStatus = status;

            return new UpdateDealResponse(true);
        }),
        Tool(AgentToolNames.GetOrder, (Guid orderId) => new UpdateDealResponse(true)),
    ];

    public static AgentTool Tool(string qualifiedName, Delegate implementation) =>
        new(qualifiedName, AIFunctionFactory.Create(implementation, name: qualifiedName[(qualifiedName.IndexOf('.', StringComparison.Ordinal) + 1)..]), false);
}
