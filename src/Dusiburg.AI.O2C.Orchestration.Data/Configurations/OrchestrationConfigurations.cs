using Dusiburg.AI.O2C.Orchestration.Data.Entities;
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
