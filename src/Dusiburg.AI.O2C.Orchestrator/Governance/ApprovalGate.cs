using System.Text.Json;
using Dusiburg.AI.O2C.Orchestrator.Tools;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Idempotency;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Governance;

/// <summary>
/// Cosa fare con una richiesta di approvazione che il framework ha esposto: proseguire subito (la policy non chiede
/// nulla, oppure il tool non è sensibile), sospendere il workflow con la proposta congelata, oppure fermarlo senza
/// approvazione quando <see cref="Stop"/> è valorizzato (SKU inesistente in ERP, D20).
/// </summary>
public sealed record ApprovalGateVerdict(bool AutoApprove, ApprovalPayload? Payload, IReadOnlyList<ApprovalReason> Reasons, AgentVerdict? Stop = null)
{
    public static ApprovalGateVerdict Continue { get; } = new(true, null, []);

    public static ApprovalGateVerdict Stopped(AgentVerdict stop) => new(false, null, [], stop);
}

/// <summary>
/// Applica la <see cref="ApprovalPolicy"/> alla chiamata che il framework ha fermato in attesa di approvazione (5.2).
/// <para>
/// Le giacenze su cui la policy decide le **verifica l'host**, riga per riga e con le quantità effettivamente proposte:
/// un agente che salta <c>check_stock</c> — o che lo chiama con la quantità sbagliata — non deve poter far passare un
/// ordine che andrebbe approvato. Le chiamate degli agenti restano nel contesto come traccia di ciò che hanno fatto,
/// ma non sono la base della decisione (§12: il garante è l'host).
/// </para>
/// <para>
/// <c>FunctionInvokingChatClient</c> chiede conferma per ogni chiamata del turno in cui compare un tool sensibile, anche
/// per i tool che non lo sono: quelli si approvano subito, senza coinvolgere nessuno.
/// </para>
/// </summary>
public sealed class ApprovalGate(ApprovalPolicy policy, ILogger<ApprovalGate> logger)
{
    /// <summary>Nome con cui il tool sensibile è esposto al modello (senza prefisso di server, R5).</summary>
    public const string SensitiveToolName = ErpToolNames.CreateOrder;

    /// <summary>Ambito dell'host per le verifiche che fa da sé.</summary>
    private static readonly AgentScope HostScope = new(AgentScope.HostName, new HashSet<string> { AgentToolNames.CheckStock });

    public async Task<ApprovalGateVerdict> EvaluateAsync(
        ToolApprovalRequestContent request,
        DealRunContext context,
        IReadOnlyList<AgentTool> tools,
        CancellationToken cancellationToken)
    {
        if (request.ToolCall is not FunctionCallContent call || call.Name != SensitiveToolName)
        {
            return ApprovalGateVerdict.Continue;
        }

        var lines = ReadLines(call.Arguments);
        StockVerification verification = await VerifyStockAsync(lines, context, tools, cancellationToken);
        IReadOnlyList<StockCheckDto> stock = verification.Checks;

        context.ReplaceStock(stock);

        // D20: uno SKU che l'ERP non conosce non è una riga da approvare in backorder ma un ordine impossibile. Lo decide
        // l'host sulla propria verifica, anche se l'agente non l'ha segnalato (D61).
        if (verification.UnknownSkus.Count > 0)
        {
            string reason = $"SKU inesistenti in ERP: {string.Join(", ", verification.UnknownSkus)}. Ordine non proponibile, nessuna approvazione (D20).";

            logger.LogWarning("Deal {DealId}: {Reason}", context.DealId, reason);

            return ApprovalGateVerdict.Stopped(new AgentVerdict(AgentScope.HostName, DealStatus.Failed, reason));
        }

        var decision = policy.Evaluate(new ApprovalContext(lines, stock, context.Customer, context.CustomerCreatedInThisRun));

        if (!decision.Required)
        {
            return ApprovalGateVerdict.Continue;
        }

        return new ApprovalGateVerdict(false, BuildPayload(lines, stock, context), decision.Reasons);
    }

    /// <summary>
    /// Verifica di giacenza fatta dall'host su ogni riga proposta. Uno SKU che l'ERP non conosce (<c>NOT_FOUND</c>) finisce
    /// fra gli SKU inesistenti; ogni altra verifica che non riesce non diventa una disponibilità: viene trattata come riga
    /// non disponibile, così l'esito peggiore è un'approvazione in più.
    /// </summary>
    private async Task<StockVerification> VerifyStockAsync(
        IReadOnlyList<OrderLineInput> lines,
        DealRunContext context,
        IReadOnlyList<AgentTool> tools,
        CancellationToken cancellationToken)
    {
        var checkStock = new GuardedToolFunction(tools.Single(t => t.QualifiedName == AgentToolNames.CheckStock), context, HostScope);
        var checks = new List<StockCheckDto>(lines.Count);
        var unknownSkus = new List<string>();

        foreach (var line in lines)
        {
            var arguments = new AIFunctionArguments { ["sku"] = line.Sku, ["quantity"] = line.Quantity };
            var result = ToolResultReader.Read(await checkStock.InvokeAsync(arguments, cancellationToken));

            if (result.Succeeded && Deserialize(result.Data) is { } check)
            {
                checks.Add(check);

                continue;
            }

            if (result.ErrorCode == ToolErrorCodes.NotFound)
            {
                unknownSkus.Add(line.Sku);
                checks.Add(new StockCheckDto(line.Sku, Available: false, OnHand: 0, LeadTimeDays: 0));

                continue;
            }

            logger.LogWarning(
                "Deal {DealId}: verifica di giacenza di {Sku} non riuscita ({ToolOutcome}): la riga conta come non disponibile",
                context.DealId, line.Sku, result.ErrorCode);

            checks.Add(new StockCheckDto(line.Sku, Available: false, OnHand: 0, LeadTimeDays: 0));
        }

        return new StockVerification(checks, unknownSkus);
    }

    private sealed record StockVerification(IReadOnlyList<StockCheckDto> Checks, IReadOnlyList<string> UnknownSkus);

    private static StockCheckDto? Deserialize(JsonElement data)
    {
        try
        {
            return data.Deserialize<StockCheckDto>(AgentJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Proposta congelata mostrata all'approvatore e, in caso di approvazione, realizzata dal workflow ripreso.
    /// La chiave di idempotenza è quella che userà <c>create_order</c>: stessa chiave, un solo ordine (D17).
    /// </summary>
    private static ApprovalPayload BuildPayload(
        IReadOnlyList<OrderLineInput> lines, IReadOnlyList<StockCheckDto> stock, DealRunContext context)
    {
        var payloadLines = lines.Select(line =>
        {
            var check = stock.FirstOrDefault(s => string.Equals(s.Sku, line.Sku, StringComparison.OrdinalIgnoreCase));

            return new ApprovalLine(line.Sku, line.Quantity, line.UnitPrice, check?.Available, check?.OnHand, check?.LeadTimeDays);
        }).ToList();

        return new ApprovalPayload(
            context.DealId,
            context.Deal?.Revision ?? 0,
            context.Deal?.Name ?? string.Empty,
            context.Company?.CompanyId ?? context.Deal?.CompanyId ?? string.Empty,
            context.Company?.Name ?? string.Empty,
            context.Customer?.CustomerId ?? 0,
            context.Customer?.Name ?? context.Company?.Name ?? string.Empty,
            context.Customer?.IsBlocked ?? false,
            context.CustomerCreatedInThisRun,
            payloadLines,
            payloadLines.Sum(l => l.LineTotal),
            IdempotencyKey.From(context.DealId, context.Deal?.Revision ?? 0));
    }

    /// <summary>
    /// Righe proposte dal modello nella chiamata fermata. Una riga illeggibile viene scartata: il totale su cui decide
    /// la policy deve venire da dati validi, e una proposta vuota resta comunque una proposta da approvare.
    /// </summary>
    internal static IReadOnlyList<OrderLineInput> ReadLines(IDictionary<string, object?>? arguments)
    {
        if (arguments?.TryGetValue("lines", out var value) != true || value is null)
        {
            return [];
        }

        var element = value as JsonElement? ?? JsonSerializer.SerializeToElement(value, AgentJson.Options);

        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<OrderLineInput>();

        foreach (var item in element.EnumerateArray())
        {
            try
            {
                if (item.Deserialize<OrderLineInput>(AgentJson.Options) is { Sku.Length: > 0 } line)
                {
                    lines.Add(line);
                }
            }
            catch (JsonException)
            {
                // Riga non conforme al contratto: non entra nel totale né nella proposta.
            }
        }

        return lines;
    }
}
