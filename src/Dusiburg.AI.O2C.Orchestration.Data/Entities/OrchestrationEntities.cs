using Dusiburg.AI.O2C.Shared.Contracts.Approvals;

namespace Dusiburg.AI.O2C.Orchestration.Data.Entities;

/// <summary>
/// Fase del workflow su un deal. Valori espliciti: sono le PK della tabella di lookup <c>orch.WorkflowPhase</c> (tinyint).
/// </summary>
public enum WorkflowPhase : byte
{
    Received = 1,
    Intake = 2,
    Fulfillment = 3,
    Order = 4,
    Completed = 5,
    Discarded = 6,
    Failed = 7,

    /// <summary>Il workflow è sospeso su una richiesta di approvazione: nulla resta in memoria (§7, Fase 5).</summary>
    AwaitingApproval = 8,

    /// <summary>
    /// Qualcuno ha preso possesso della ripresa e la sta eseguendo. È una fase di passaggio che dura quanto il run
    /// ripreso, e serve a impedire che due consegne della stessa decisione — il messaggio e la sweep — riprendano
    /// lo stesso workflow in parallelo.
    /// </summary>
    Resuming = 9
}

/// <summary>
/// Stato persistito di un workflow (§8, 4.4): una riga per evento <c>deal-closed-won</c> elaborato.
/// L'indice univoco su deal e revisione rende idempotente il consumer: lo stesso evento non avvia un secondo workflow.
/// </summary>
public sealed class WorkflowState
{
    public int Id { get; set; }

    /// <summary>Correlation id del workflow, univoco (colonna dedicata, non PK: D29). È anche la sessione dei checkpoint.</summary>
    public required string CorrelationId { get; set; }

    public required string DealId { get; set; }

    public int DealRevision { get; set; }

    /// <summary>Colonna <c>WorkflowPhaseId</c>, FK verso <c>orch.WorkflowPhase</c>.</summary>
    public WorkflowPhase Phase { get; set; }

    /// <summary>Fatti del run (risultati dei tool, handoff, esito) serializzati in JSON.</summary>
    public string? StateJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Richiesta di approvazione su <c>erp.create_order</c> (§7, 5.3). <see cref="PublicId"/> è il GUID esposto dalla UI,
/// dal callback e dal messaggio <c>approval-decided</c>: la PK resta numerica (D29).
/// Le transizioni sono ammesse solo da <see cref="ApprovalStatus.Pending"/> e usano <see cref="RowVersion"/>,
/// così una decisione e una scadenza concorrenti non possono sovrapporsi.
/// </summary>
public sealed class ApprovalRequest
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    /// <summary>Correlation id del workflow sospeso: collega richiesta, <see cref="WorkflowState"/> e checkpoint.</summary>
    public required string CorrelationId { get; set; }

    public required string DealId { get; set; }

    public int DealRevision { get; set; }

    /// <summary>Proposta di ordine congelata (<see cref="ApprovalPayload"/> serializzato), mostrata all'approvatore.</summary>
    public required string PayloadJson { get; set; }

    /// <summary>Motivi di §7 che hanno richiesto l'approvazione, come array JSON di <see cref="ApprovalReason"/>.</summary>
    public required string ReasonsJson { get; set; }

    /// <summary>Totale della proposta, ridondato per l'elenco della UI senza deserializzare il payload.</summary>
    public decimal Total { get; set; }

    /// <summary>Colonna <c>ApprovalStatusId</c>, FK verso <c>orch.ApprovalStatus</c>.</summary>
    public ApprovalStatus Status { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>UPN dell'approvatore, da configurazione in locale (D25).</summary>
    public string? DecidedBy { get; set; }

    public string? DecisionNote { get; set; }

    /// <summary>
    /// Contesto W3C dello span che ha generato la richiesta (G5.4, M12): la ripresa lo usa come parent,
    /// così l'attesa di ore resta dentro un'unica traccia.
    /// </summary>
    public string? TraceParent { get; set; }

    /// <summary>Checkpoint del workflow da cui riprendere dopo l'approvazione (meccanismo A, D16).</summary>
    public string? CheckpointId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Un checkpoint del workflow prodotto da Agent Framework (<c>ICheckpointStore&lt;JsonElement&gt;</c>): la sessione è il
/// correlation id del workflow. <see cref="Id"/> crescente conserva l'ordine di commit richiesto dallo store.
/// </summary>
public sealed class WorkflowCheckpoint
{
    public long Id { get; set; }

    public required string SessionId { get; set; }

    public required string CheckpointId { get; set; }

    public string? ParentCheckpointId { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Riga di una tabella di lookup generata da un enum: <see cref="Id"/> è il valore numerico esplicito nel codice,
/// <see cref="Name"/> il nome del membro. Le righe vengono da <c>Enum.GetValues</c>, mai scritte a mano.
/// </summary>
public sealed class EnumLookup<TEnum> where TEnum : struct, Enum
{
    public TEnum Id { get; set; }

    public required string Name { get; set; }
}
