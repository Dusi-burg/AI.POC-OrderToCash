using System.ComponentModel;
using Dusiburg.AI.O2C.Erp.Mcp.Erp;
using Dusiburg.AI.O2C.Mcp.Hosting;
using Dusiburg.AI.O2C.Shared.Contracts;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Errors;
using ModelContextProtocol.Server;

namespace Dusiburg.AI.O2C.Erp.Mcp.Tools;

/// <summary>
/// Tool di <c>erp-mcp</c> (§6.1) sopra <c>Erp.Api</c>. Gli errori sono <see cref="ToolException"/>,
/// restituiti come <c>{ error: { code, message } }</c> dal filtro comune (D33).
/// </summary>
[McpServerToolType]
public sealed class ErpTools(ErpApiClient erp)
{
    [McpServerTool(Name = ErpToolNames.GetCustomer, Title = "Cerca cliente ERP",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Cerca un cliente nell'anagrafica ERP per partita IVA oppure per email: fornire almeno uno dei due "
        + "(se ci sono entrambi vale prima la partita IVA). Restituisce { customer }: customer è null se il cliente non esiste, "
        + "e non è un errore. isBlocked = true indica un cliente bloccato.")]
    public async Task<GetCustomerResponse> GetCustomerAsync(
        [Description("Partita IVA del cliente, es. IT01234560157.")] string? vatNumber = null,
        [Description("Email del cliente.")] string? email = null,
        CancellationToken cancellationToken = default)
    {
        vatNumber = string.IsNullOrWhiteSpace(vatNumber) ? null : vatNumber.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        if (vatNumber is null && email is null)
        {
            throw new ToolException(ToolErrorCodes.ValidationError, "Fornire almeno uno fra vatNumber ed email.");
        }

        return new GetCustomerResponse(await erp.GetCustomerAsync(vatNumber, email, cancellationToken));
    }

    [McpServerTool(Name = ErpToolNames.CreateCustomer, Title = "Crea cliente ERP",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Crea un cliente nell'anagrafica ERP e restituisce { customerId }. Usarlo solo dopo aver verificato con "
        + "get_customer che il cliente non esista: una partita IVA già registrata dà errore CONFLICT.")]
    public async Task<CreateCustomerResponse> CreateCustomerAsync(
        [Description("Ragione sociale, max 200 caratteri.")] string name,
        [Description("Partita IVA, max 20 caratteri.")] string vatNumber,
        [Description("Email, max 254 caratteri.")] string email,
        [Description("Indirizzo completo, max 400 caratteri.")] string address,
        CancellationToken cancellationToken = default)
    {
        return await erp.CreateCustomerAsync(new CreateCustomerRequest(name, vatNumber, email, address), cancellationToken);
    }

    [McpServerTool(Name = ErpToolNames.CheckStock, Title = "Verifica giacenza ERP",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Verifica se uno SKU è disponibile per una quantità. available è true se la giacenza libera "
        + "(onHand meno le quantità già riservate da altri ordini) copre la quantità richiesta; onHand è la giacenza fisica, "
        + "leadTimeDays i giorni di riassortimento. SKU inesistente in ERP: errore NOT_FOUND.")]
    public async Task<StockCheckDto> CheckStockAsync(
        [Description("Codice articolo, es. IND-BRG-001.")] string sku,
        [Description("Quantità richiesta, maggiore di zero.")] int quantity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ToolException(ToolErrorCodes.ValidationError, "sku è obbligatorio.");
        }

        return await erp.CheckStockAsync(sku.Trim(), quantity, cancellationToken);
    }

    [McpServerTool(Name = ErpToolNames.CreateOrder, Title = "Crea ordine ERP",
        ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.Sensitive, true)]
    [Description("Crea un ordine di vendita nell'ERP e riserva lo stock delle righe. Operazione sensibile. "
        + "Le righe usano il prezzo unitario del deal. Con la stessa idempotencyKey restituisce l'ordine già creato invece di crearne "
        + "un secondo. status è Confirmed se tutte le righe erano disponibili, altrimenti Backorder. "
        + "Cliente o SKU inesistenti: errore NOT_FOUND.")]
    public async Task<CreateOrderResponse> CreateOrderAsync(
        [Description("customerId del cliente ERP, da get_customer o create_customer.")] int customerId,
        [Description("Righe dell'ordine, da 1 a 100: sku, quantity maggiore di zero, unitPrice del deal con al massimo due decimali.")]
        IReadOnlyList<OrderLineInput> lines,
        [Description("Riferimento esterno: il dealId del CRM, es. D-1001.")] string externalRef,
        [Description("Chiave di idempotenza fornita dall'orchestratore (formato o2c-<dealId>-r<revision>): non va inventata.")]
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await erp.CreateOrderAsync(new CreateOrderRequest(customerId, lines, externalRef, idempotencyKey), cancellationToken);
    }

    [McpServerTool(Name = ErpToolNames.GetOrder, Title = "Leggi ordine ERP",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Legge un ordine ERP completo di righe dato l'orderId (GUID restituito da create_order). "
        + "Ordine inesistente: errore NOT_FOUND.")]
    public async Task<OrderDto> GetOrderAsync(
        [Description("orderId dell'ordine, GUID.")] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return await erp.GetOrderAsync(orderId, cancellationToken);
    }
}
