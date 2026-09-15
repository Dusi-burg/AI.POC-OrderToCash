using System.ComponentModel;
using Dusiburg.AI.O2C.Crm.Mcp.Crm;
using Dusiburg.AI.O2C.Mcp.Hosting;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Errors;
using ModelContextProtocol.Server;

namespace Dusiburg.AI.O2C.Crm.Mcp.Tools;

/// <summary>
/// Tool di <c>crm-mcp</c> (§6.2) sopra <see cref="ICrmClient"/>. Gli errori sono <see cref="ToolException"/>,
/// restituiti come <c>{ error: { code, message } }</c> dal filtro comune (D33).
/// </summary>
[McpServerToolType]
public sealed class CrmTools(ICrmClient crm)
{
    [McpServerTool(Name = CrmToolNames.GetDeal, Title = "Leggi deal CRM",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Legge un deal del CRM: nome, importo, valuta, stage, azienda (companyId), righe (sku, quantity, unitPrice) "
        + "e revision. revision cresce solo con le modifiche commerciali del deal. Deal inesistente: errore NOT_FOUND.")]
    public async Task<DealDto> GetDealAsync(
        [Description("Identificativo del deal, es. D-1001.")] string dealId,
        CancellationToken cancellationToken = default)
    {
        dealId = Require(dealId, nameof(dealId));

        return await crm.GetDealAsync(dealId, cancellationToken)
            ?? throw new ToolException(ToolErrorCodes.NotFound, $"Deal {dealId} non trovato.");
    }

    [McpServerTool(Name = CrmToolNames.GetCompany, Title = "Leggi azienda CRM",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Legge l'anagrafica di un'azienda del CRM (nome, partita IVA, email, indirizzo) dato il companyId del deal. "
        + "Azienda inesistente: errore NOT_FOUND.")]
    public async Task<CompanyDto> GetCompanyAsync(
        [Description("Identificativo dell'azienda, es. C-01.")] string companyId,
        CancellationToken cancellationToken = default)
    {
        companyId = Require(companyId, nameof(companyId));

        return await crm.GetCompanyAsync(companyId, cancellationToken)
            ?? throw new ToolException(ToolErrorCodes.NotFound, $"Azienda {companyId} non trovata.");
    }

    [McpServerTool(Name = CrmToolNames.UpdateDeal, Title = "Aggiorna stato O2C del deal",
        ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Scrive sul deal lo stato del processo Order-to-Cash, il numero dell'ordine ERP se creato e una nota; "
        + "ogni chiamata aggiunge una voce allo storico del deal e non modifica revision. Restituisce { ok }. "
        + "Deal inesistente: errore NOT_FOUND.")]
    public async Task<UpdateDealResponse> UpdateDealAsync(
        [Description("Identificativo del deal, es. D-1001.")] string dealId,
        [Description("Stato O2C da scrivere: ApprovalPending, OrderCreated, Rejected, Expired, Discarded oppure Failed.")]
        DealStatus status,
        [Description("Numero dell'ordine ERP, es. SO-2026-000001; solo se l'ordine è stato creato.")] string? erpOrderNumber = null,
        [Description("Nota per il commerciale, max 1000 caratteri.")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        dealId = Require(dealId, nameof(dealId));

        try
        {
            return await crm.UpdateDealAsync(new UpdateDealRequest(dealId, erpOrderNumber, status, note), cancellationToken)
                ?? throw new ToolException(ToolErrorCodes.NotFound, $"Deal {dealId} non trovato.");
        }
        catch (ArgumentException exception)
        {
            throw new ToolException(ToolErrorCodes.ValidationError, exception.Message, exception);
        }
    }

    private static string Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ToolException(ToolErrorCodes.ValidationError, $"{name} è obbligatorio.");
        }

        return value.Trim();
    }
}
