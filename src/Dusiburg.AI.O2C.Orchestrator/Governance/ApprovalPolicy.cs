using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Demo;

namespace Dusiburg.AI.O2C.Orchestrator.Governance;

/// <summary>
/// Dati su cui la policy decide: righe **proposte** dall'agente, verifiche di giacenza raccolte dal workflow e cliente
/// risolto in ERP. Il totale non arriva dal modello: lo calcola la policy dalle righe (§12, D17).
/// </summary>
public sealed record ApprovalContext(
    IReadOnlyList<OrderLineInput> Lines,
    IReadOnlyList<StockCheckDto> Stock,
    CustomerDto? Customer,
    bool CustomerCreatedInThisRun)
{
    public decimal Total => Lines.Sum(line => line.Quantity * line.UnitPrice);
}

/// <summary>Esito della policy: se serve l'approvazione e per quali regole di §7.</summary>
public sealed record ApprovalDecision(bool Required, IReadOnlyList<ApprovalReason> Reasons)
{
    public static ApprovalDecision NotRequired { get; } = new(false, []);
}

/// <summary>
/// Regole di approvazione di §7 (5.1): deterministiche, in C#, **unica** fonte di verità — i prompt degli agenti non
/// contengono soglie né condizioni. La soglia si legge da <c>APPROVAL_THRESHOLD_EUR</c> a ogni valutazione.
/// </summary>
public sealed class ApprovalPolicy(IConfiguration configuration)
{
    public const string ThresholdSetting = "APPROVAL_THRESHOLD_EUR";

    /// <summary>Soglia di default di §7, la stessa attorno a cui sono costruiti gli scenari demo.</summary>
    public const decimal DefaultThresholdEur = DemoCatalog.ApprovalThresholdEur;

    public decimal ThresholdEur =>
        decimal.TryParse(configuration[ThresholdSetting], System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : DefaultThresholdEur;

    public ApprovalDecision Evaluate(ApprovalContext context)
    {
        var reasons = new List<ApprovalReason>();

        // Confronto stretto: un ordine esattamente pari alla soglia non richiede approvazione.
        if (context.Total > ThresholdEur)
        {
            reasons.Add(ApprovalReason.OverThreshold);
        }

        // Basta una riga non disponibile: l'ordine andrebbe comunque creato, in backorder (D20).
        if (context.Lines.Any(line => IsInsufficient(context.Stock, line)))
        {
            reasons.Add(ApprovalReason.InsufficientStock);
        }

        if (context.CustomerCreatedInThisRun)
        {
            reasons.Add(ApprovalReason.NewCustomer);
        }

        if (context.Customer is { IsBlocked: true })
        {
            reasons.Add(ApprovalReason.BlockedCustomer);
        }

        return reasons.Count == 0 ? ApprovalDecision.NotRequired : new ApprovalDecision(true, reasons);
    }

    /// <summary>
    /// Una riga è insufficiente quando la verifica di giacenza sul suo SKU dice <c>available = false</c>.
    /// Una riga senza verifica non è una prova di indisponibilità: non genera da sola una richiesta di approvazione.
    /// </summary>
    private static bool IsInsufficient(IReadOnlyList<StockCheckDto> stock, OrderLineInput line) =>
        stock.Any(check => string.Equals(check.Sku, line.Sku, StringComparison.OrdinalIgnoreCase) && !check.Available);
}
