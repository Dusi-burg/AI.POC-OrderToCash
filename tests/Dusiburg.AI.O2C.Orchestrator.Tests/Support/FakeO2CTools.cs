using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Errors;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Support;

/// <summary>
/// Tool ERP e CRM in memoria con le stesse firme dei server MCP; registrano gli argomenti ricevuti.
/// Deal, SKU inesistente, SKU senza giacenza e cliente ERP sono configurabili per gli scenari del workflow
/// e per le quattro regole di approvazione di §7.
/// <para>
/// I tool restituiscono **testo JSON**, come fanno i tool MCP veri: il marshaller a reflection di
/// <c>AIFunctionFactory</c> serializza i risultati non-stringa su una pipe che, dopo un run sospeso in attesa di
/// approvazione, non si è mai svuotata dentro il runner di NUnit. Con il testo il doppio è anche più fedele a MCP.
/// </para>
/// </summary>
internal sealed class FakeO2CTools(
    DealDto? deal = null,
    string? unknownSku = null,
    string? unavailableSku = null,
    CustomerDto? customer = null,
    bool customerExists = true)
{
    public const string OrderNumber = "SO-2026-000001";

    public static readonly DealDto Deal = new("D-1001", 3, "Ricambi cuscinetti linea 2", 480m, "EUR", "ClosedWon", "C-01",
        [new DealLineItemDto("IND-BRG-001", 40, 12m)]);

    public static readonly CustomerDto Customer =
        new(1, "Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "acquisti@brambilla-om.it", 50_000m, false);

    private readonly DealDto _deal = deal ?? Deal;
    private readonly CustomerDto _customer = customer ?? Customer;

    public Dictionary<string, object?>? CreateOrderArguments { get; private set; }

    public int CreateOrderCalls { get; private set; }

    public int CreateCustomerCalls { get; private set; }

    public DealStatus? UpdatedStatus { get; private set; }

    public string? UpdatedNote { get; private set; }

    public List<DealStatus> UpdatedStatuses { get; } = [];

    public AgentTool[] All() =>
    [
        Tool(AgentToolNames.GetDeal, (string dealId) => Json(_deal)),
        Tool(AgentToolNames.GetCompany, (string companyId) => Json(
            new CompanyDto(companyId, "Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "acquisti@brambilla-om.it", "Via dell'Industria 12"))),
        Tool(AgentToolNames.CheckStock, (string sku, int quantity) => sku == unknownSku
            ? Json(ToolErrorResponse.Create(ToolErrorCodes.NotFound, $"SKU {sku} non trovato."))
            : Json(sku == unavailableSku ? new StockCheckDto(sku, false, 0, 30) : new StockCheckDto(sku, true, 500, 3))),
        Tool(AgentToolNames.GetCustomer, (string? vatNumber = null, string? email = null) =>
            Json(new GetCustomerResponse(customerExists ? _customer : null))),
        Tool(AgentToolNames.CreateCustomer, (string name, string vatNumber, string email, string address) =>
        {
            CreateCustomerCalls++;

            return Json(new CreateCustomerResponse(99));
        }),
        Tool(AgentToolNames.CreateOrder, (int customerId, List<OrderLineInput> lines, string externalRef, string idempotencyKey) =>
        {
            CreateOrderCalls++;
            CreateOrderArguments = new() { ["customerId"] = customerId, ["externalRef"] = externalRef, ["idempotencyKey"] = idempotencyKey, ["lines"] = lines };

            // Come l'ERP: se una riga non è coperta l'ordine nasce in backorder e dice cosa manca (M25).
            var backorder = lines.Where(l => l.Sku == unavailableSku).Select(l => $"{l.Sku}: {l.Quantity} PZ da ordinare").ToList();

            return Json(new CreateOrderResponse(
                Guid.Parse("0199a000-0000-7000-8000-000000000001"),
                OrderNumber,
                480m,
                backorder.Count == 0 ? OrderStatus.Confirmed : OrderStatus.Backorder,
                backorder.Count == 0 ? null : string.Join("; ", backorder)));
        }),
        Tool(AgentToolNames.UpdateDeal, (string dealId, DealStatus status, string? erpOrderNumber = null, string? note = null) =>
        {
            UpdatedStatus = status;
            UpdatedNote = note;
            UpdatedStatuses.Add(status);

            return Json(new UpdateDealResponse(true));
        }),
        Tool(AgentToolNames.GetOrder, (Guid orderId) => Json(new UpdateDealResponse(true))),
    ];

    public static AgentTool Tool(string qualifiedName, Delegate implementation) =>
        new(qualifiedName, AIFunctionFactory.Create(implementation, name: qualifiedName[(qualifiedName.IndexOf('.', StringComparison.Ordinal) + 1)..]), false);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
}
