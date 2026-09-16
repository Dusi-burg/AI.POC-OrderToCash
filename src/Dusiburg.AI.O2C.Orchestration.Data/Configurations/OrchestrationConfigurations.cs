using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dusiburg.AI.O2C.Orchestration.Data.Configurations;

// Convenzioni (D29-D31): tabelle al singolare, PK numerica "Id", chiavi di business come colonne univoche,
// enum come FK verso tabelle di lookup tinyint generate dal codice. Codici e chiavi in varchar, testi liberi in nvarchar.

/// <summary>Tabella di lookup di un enum: PK tinyint = valore esplicito del membro, righe da <c>Enum.GetValues</c>.</summary>
internal abstract class EnumLookupConfiguration<TEnum>(string tableName) : IEntityTypeConfiguration<EnumLookup<TEnum>>
    where TEnum : struct, Enum
{
    public void Configure(EntityTypeBuilder<EnumLookup<TEnum>> builder)
    {
        builder.ToTable(tableName);

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).HasConversion<byte>().ValueGeneratedNever();
        builder.Property(l => l.Name).HasMaxLength(50).IsUnicode(false);

        builder.HasIndex(l => l.Name).IsUnique();

        builder.HasData(Enum.GetValues<TEnum>().Select(value => new EnumLookup<TEnum> { Id = value, Name = value.ToString() }));
    }
}

internal sealed class WorkflowPhaseConfiguration() : EnumLookupConfiguration<WorkflowPhase>("WorkflowPhase");

internal sealed class WorkflowStateConfiguration : IEntityTypeConfiguration<WorkflowState>
{
    public void Configure(EntityTypeBuilder<WorkflowState> builder)
    {
        builder.ToTable("WorkflowState", t => t.HasCheckConstraint("CK_WorkflowState_DealRevision", "[DealRevision] >= 0"));

        builder.HasKey(s => s.Id);

        builder.Property(s => s.CorrelationId).HasMaxLength(128).IsUnicode(false);
        builder.Property(s => s.DealId).HasMaxLength(20).IsUnicode(false);
        builder.Property(s => s.Phase).HasColumnName("WorkflowPhaseId").HasConversion<byte>();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.CorrelationId).IsUnique();
        builder.HasIndex(s => new { s.DealId, s.DealRevision }).IsUnique();

        builder.HasOne<EnumLookup<WorkflowPhase>>()
            .WithMany()
            .HasForeignKey(s => s.Phase)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ApprovalStatusConfiguration() : EnumLookupConfiguration<ApprovalStatus>("ApprovalStatus");

internal sealed class ApprovalReasonConfiguration() : EnumLookupConfiguration<ApprovalReason>("ApprovalReason");

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequest", t => t.HasCheckConstraint("CK_ApprovalRequest_Total", "[Total] >= 0"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.CorrelationId).HasMaxLength(128).IsUnicode(false);
        builder.Property(r => r.DealId).HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.Total).HasPrecision(18, 2);
        builder.Property(r => r.Status).HasColumnName("ApprovalStatusId").HasConversion<byte>();
        builder.Property(r => r.DecidedBy).HasMaxLength(256);
        builder.Property(r => r.DecisionNote).HasMaxLength(1000);
        builder.Property(r => r.TraceParent).HasMaxLength(64).IsUnicode(false);
        builder.Property(r => r.CheckpointId).HasMaxLength(64).IsUnicode(false);
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.PublicId).IsUnique();
        builder.HasIndex(r => r.CorrelationId);

        // Elenco della UI e sweep di scadenza: entrambi filtrano per stato e ordinano per data di richiesta.
        builder.HasIndex(r => new { r.Status, r.RequestedAt });

        builder.HasOne<EnumLookup<ApprovalStatus>>()
            .WithMany()
            .HasForeignKey(r => r.Status)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkflowCheckpointConfiguration : IEntityTypeConfiguration<WorkflowCheckpoint>
{
    public void Configure(EntityTypeBuilder<WorkflowCheckpoint> builder)
    {
        builder.ToTable("WorkflowCheckpoint");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.SessionId).HasMaxLength(128).IsUnicode(false);
        builder.Property(c => c.CheckpointId).HasMaxLength(64).IsUnicode(false);
        builder.Property(c => c.ParentCheckpointId).HasMaxLength(64).IsUnicode(false);

        builder.HasIndex(c => new { c.SessionId, c.CheckpointId }).IsUnique();
    }
}
