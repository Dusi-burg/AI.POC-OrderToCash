using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.Shared.Telemetry;

namespace Dusiburg.AI.O2C.Approvals.Web.Approvals;

/// <summary>Richiesta pronta per la UI: riepilogo, motivi e proposta di ordine già deserializzati.</summary>
public sealed record ApprovalView(ApprovalSummary Summary, ApprovalPayload Payload, int DealRevision);

/// <summary>
/// Lettura e decisione delle richieste di approvazione (5.4). La transizione è ammessa solo da <c>Pending</c> ed è
/// protetta da <c>RowVersion</c>: una seconda decisione non sovrascrive la prima. Dopo il salvataggio si pubblica
/// <c>approval-decided</c>, che per l'orchestratore è solo un acceleratore della ripresa (G5.3).
/// </summary>
public sealed class ApprovalDecisionService(
    ApprovalRepository repository,
    IApprovalDecisionPublisher publisher,
    IConfiguration configuration,
    ILogger<ApprovalDecisionService> logger)
{
    /// <summary>Approvatore configurato in locale; in Fase 6 arriverà da Entra (D25).</summary>
    public const string ApproverSetting = "Approvals:ApproverUpn";

    public const string DefaultApprover = "approver@dusiburg.local";

    public string Approver => configuration[ApproverSetting] is { Length: > 0 } upn ? upn : DefaultApprover;

    public async Task<IReadOnlyList<ApprovalView>> ListAsync(ApprovalStatus? status, CancellationToken cancellationToken) =>
        [.. (await repository.ListAsync(status, cancellationToken)).Select(Map)];

    public async Task<ApprovalView?> FindAsync(Guid approvalId, CancellationToken cancellationToken) =>
        await repository.FindAsync(approvalId, cancellationToken) is { } request ? Map(request) : null;

    /// <summary>
    /// Registra la decisione e la annuncia. Il messaggio parte solo dopo il commit: se la pubblicazione fallisce
    /// la decisione resta comunque scritta, e la sweep di riconciliazione dell'orchestratore la trova.
    /// </summary>
    public async Task<(ApprovalTransition Transition, ApprovalDecisionResponse? Response)> DecideAsync(
        Guid approvalId, bool approved, string? note, CancellationToken cancellationToken)
    {
        var request = await repository.FindAsync(approvalId, cancellationToken);

        if (request is null)
        {
            return (ApprovalTransition.NotFound, null);
        }

        var decision = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        var approver = Approver;

        using var activity = ApprovalsTelemetry.Source.StartActivity(ApprovalsTelemetry.DecidedActivityName);
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalId, approvalId);
        activity?.SetTag(O2CTelemetry.Attributes.CorrelationId, request.CorrelationId);
        activity?.SetTag(O2CTelemetry.Attributes.DealId, request.DealId);
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalDecision, decision.ToString());
        activity?.SetTag(O2CTelemetry.Attributes.ApprovalDecidedBy, approver);

        var transition = await repository.TryDecideAsync(request, decision, approver, Trim(note), cancellationToken);

        if (transition != ApprovalTransition.Applied)
        {
            logger.LogWarning("Decisione su {ApprovalId} non applicata: {Transition}", approvalId, transition);

            return (transition, null);
        }

        var decidedAt = request.DecidedAt ?? DateTimeOffset.UtcNow;

        logger.LogInformation(
            "Richiesta {ApprovalId} sul deal {DealId}: {ApprovalDecision} da {DecidedBy}", approvalId, request.DealId, decision, approver);

        await publisher.PublishAsync(
            new ApprovalDecided(request.PublicId, request.CorrelationId, request.DealId, decision, decidedAt), cancellationToken);

        return (transition, new ApprovalDecisionResponse(approvalId, decision, decidedAt, approver));
    }

    private static string? Trim(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Length > 1000 ? note[..1000] : note.Trim();

    private static ApprovalView Map(ApprovalRequest request)
    {
        var reasons = JsonSerializer.Deserialize<List<ApprovalReason>>(request.ReasonsJson, JsonSerializerOptions.Web) ?? [];

        var payload = JsonSerializer.Deserialize<ApprovalPayload>(request.PayloadJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Payload della richiesta {request.PublicId} illeggibile.");

        var summary = new ApprovalSummary(
            request.PublicId, request.DealId, request.CorrelationId, request.Status, reasons, request.Total,
            request.RequestedAt, request.DecidedAt, request.DecidedBy, request.DecisionNote);

        return new ApprovalView(summary, payload, request.DealRevision);
    }
}

/// <summary>Sorgente di telemetria di <c>Approvals.Web</c> (5.7).</summary>
internal static class ApprovalsTelemetry
{
    /// <summary>Span della decisione umana, con esito e decisore.</summary>
    public const string DecidedActivityName = "approval.decided";

    public static readonly ActivitySource Source = new(O2CTelemetry.Sources.Approvals);
}
