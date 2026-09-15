namespace Dusiburg.AI.O2C.Orchestration.Data.Entities;

/// <summary>
/// Fase del workflow su un deal. Valori espliciti: sono le PK della tabella di lookup <c>orch.WorkflowPhase</c> (tinyint).
/// <c>AwaitingApproval</c> arriva con la Fase 5.
/// </summary>
public enum WorkflowPhase : byte
{
    Received = 1,
    Intake = 2,
    Fulfillment = 3,
    Order = 4,
    Completed = 5,
    Discarded = 6,
    Failed = 7
}

/// <summary>
/// Stato persistito di un workflow (§8, 4.4): una riga per evento <c>deal-closed-won</c> elaborato.
/// L'indice univoco su deal e revisione rende idempotente il consumer: lo stesso evento non avvia un secondo workflow.
/// </summary>
public sealed class WorkflowState
{
    public int Id { get; set; }

    /// <summary>Correlation id del workflow, univoco (colonna dedicata, non PK: D29).</summary>
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
/// Riga di una tabella di lookup generata da un enum: <see cref="Id"/> è il valore numerico esplicito nel codice,
/// <see cref="Name"/> il nome del membro. Le righe vengono da <c>Enum.GetValues</c>, mai scritte a mano.
/// </summary>
public sealed class EnumLookup<TEnum> where TEnum : struct, Enum
{
    public TEnum Id { get; set; }

    public required string Name { get; set; }
}
