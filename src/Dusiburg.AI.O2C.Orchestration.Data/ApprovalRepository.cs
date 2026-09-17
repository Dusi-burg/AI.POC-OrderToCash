using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Orchestration.Data;

/// <summary>Esito del tentativo di decidere una richiesta: le transizioni sono ammesse solo da <c>Pending</c> (5.4).</summary>
public enum ApprovalTransition
{
    /// <summary>La transizione è stata scritta.</summary>
    Applied,

    /// <summary>La richiesta non esiste.</summary>
    NotFound,

    /// <summary>La richiesta era già decisa o scaduta, oppure lo è diventata durante il salvataggio (409).</summary>
    AlreadyDecided
}

/// <summary>
/// Accesso a <c>orch.ApprovalRequest</c> condiviso fra l'orchestratore (creazione, ripresa, scadenza) e
/// <c>Approvals.Web</c> (elenco e decisione): le regole di transizione stanno scritte una volta sola.
/// </summary>
public sealed class ApprovalRepository(OrchestrationDbContext db, TimeProvider timeProvider)
{
    public Task<ApprovalRequest?> FindAsync(Guid approvalId, CancellationToken cancellationToken) =>
        db.ApprovalRequests.SingleOrDefaultAsync(r => r.PublicId == approvalId, cancellationToken);

    /// <summary>
    /// Richieste per l'elenco della UI, dalla più recente; con <paramref name="status"/> nullo le mostra tutte, con
    /// <paramref name="dealId"/> solo quelle del deal (link da Crm.Web, G6.8).
    /// </summary>
    public Task<List<ApprovalRequest>> ListAsync(ApprovalStatus? status, string? dealId, CancellationToken cancellationToken) =>
        db.ApprovalRequests
            .Where(r => status == null || r.Status == status)
            .Where(r => dealId == null || r.DealId == dealId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Richieste già decise il cui workflow non è ancora stato ripreso: è la sweep di riconciliazione (G5.3), che rende
    /// il messaggio <c>approval-decided</c> un acceleratore e non un punto di rottura.
    /// <para>
    /// Comprende anche i workflow rimasti in <see cref="WorkflowPhase.Resuming"/> da prima di
    /// <paramref name="staleResumeBefore"/>: è il possesso di un processo morto durante la ripresa, che senza questo
    /// recupero resterebbe appeso per sempre.
    /// </para>
    /// </summary>
    public Task<List<ApprovalRequest>> ListAwaitingResumeAsync(DateTimeOffset staleResumeBefore, CancellationToken cancellationToken) =>
        db.ApprovalRequests
            .Where(r => r.Status != ApprovalStatus.Pending)
            .Join(
                db.WorkflowStates.Where(s =>
                    s.Phase == WorkflowPhase.AwaitingApproval
                    || (s.Phase == WorkflowPhase.Resuming && s.UpdatedAt <= staleResumeBefore)),
                r => r.CorrelationId,
                s => s.CorrelationId,
                (r, _) => r)
            .OrderBy(r => r.DecidedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Prende possesso della ripresa con un UPDATE condizionale: il workflow passa a <see cref="WorkflowPhase.Resuming"/>
    /// solo se era in attesa, oppure se un possesso precedente è più vecchio di <paramref name="staleResumeBefore"/>.
    /// Chi non tocca nessuna riga ha perso la corsa e non deve fare nulla. È qui che sta l'idempotenza della ripresa:
    /// una lettura seguita da un'azione non basta, perché due consegne simultanee leggerebbero entrambe "in attesa".
    /// </summary>
    public async Task<bool> TryClaimResumeAsync(string correlationId, DateTimeOffset staleResumeBefore, CancellationToken cancellationToken)
    {
        var claimed = await db.WorkflowStates
            .Where(s => s.CorrelationId == correlationId
                && (s.Phase == WorkflowPhase.AwaitingApproval
                    || (s.Phase == WorkflowPhase.Resuming && s.UpdatedAt <= staleResumeBefore)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Phase, WorkflowPhase.Resuming)
                .SetProperty(x => x.UpdatedAt, timeProvider.GetUtcNow()), cancellationToken);

        return claimed == 1;
    }

    /// <summary>Richieste ancora pendenti oltre la scadenza (5.6).</summary>
    public Task<List<ApprovalRequest>> ListExpiredAsync(DateTimeOffset notRequestedAfter, CancellationToken cancellationToken) =>
        db.ApprovalRequests
            .Where(r => r.Status == ApprovalStatus.Pending && r.RequestedAt <= notRequestedAfter)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Porta una richiesta da <c>Pending</c> allo stato deciso. La concorrenza ottimistica su <c>RowVersion</c> fa sì che
    /// una seconda decisione — o una scadenza che arriva nello stesso momento — non sovrascriva la prima.
    /// </summary>
    public async Task<ApprovalTransition> TryDecideAsync(
        Guid approvalId, ApprovalStatus decision, string? decidedBy, string? note, CancellationToken cancellationToken)
    {
        var request = await FindAsync(approvalId, cancellationToken);

        if (request is null)
        {
            return ApprovalTransition.NotFound;
        }

        return await TryDecideAsync(request, decision, decidedBy, note, cancellationToken);
    }

    public async Task<ApprovalTransition> TryDecideAsync(
        ApprovalRequest request, ApprovalStatus decision, string? decidedBy, string? note, CancellationToken cancellationToken)
    {
        if (request.Status != ApprovalStatus.Pending)
        {
            return ApprovalTransition.AlreadyDecided;
        }

        request.Status = decision;
        request.DecidedAt = timeProvider.GetUtcNow();
        request.DecidedBy = decidedBy;
        request.DecisionNote = note;

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return ApprovalTransition.Applied;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();

            return ApprovalTransition.AlreadyDecided;
        }
    }

    /// <summary>Segna il workflow come concluso dopo che la ripresa (o il rifiuto) ha scritto l'esito sul CRM.</summary>
    public Task<int> CloseWorkflowAsync(string correlationId, WorkflowPhase phase, CancellationToken cancellationToken) =>
        db.WorkflowStates
            .Where(s => s.CorrelationId == correlationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Phase, phase)
                .SetProperty(x => x.UpdatedAt, timeProvider.GetUtcNow()), cancellationToken);
}
