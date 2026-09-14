using Dusiburg.AI.O2C.Crm.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dusiburg.AI.O2C.Crm.Data.Configurations;

// Convenzioni (D29-D31): tabelle al singolare, PK numerica "Id", FK qualificate (<Entity>Id), chiavi di business stringa come colonne univoche,
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

internal sealed class DealStageConfiguration() : EnumLookupConfiguration<DealStage>("DealStage");

internal sealed class DealStatusConfiguration() : EnumLookupConfiguration<DealStatus>("DealStatus");

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Company");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.VatNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(254).IsUnicode(false);
        builder.Property(c => c.Address).HasMaxLength(400);

        builder.HasIndex(c => c.Code).IsUnique();
    }
}

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> builder)
    {
        builder.ToTable("Deal", t =>
        {
            t.HasCheckConstraint("CK_Deal_Amount", "[Amount] >= 0");
            t.HasCheckConstraint("CK_Deal_Revision", "[Revision] >= 0");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasMaxLength(20).IsUnicode(false);
        builder.Property(d => d.Name).HasMaxLength(200);
        builder.Property(d => d.Amount).HasPrecision(18, 2);
        builder.Property(d => d.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(d => d.Stage).HasColumnName("DealStageId").HasConversion<byte>();
        builder.Property(d => d.ErpOrderNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(d => d.O2CStatus).HasColumnName("DealStatusId").HasConversion<byte>();
        builder.Property(d => d.LastNote).HasMaxLength(1000);

        builder.HasIndex(d => d.Code).IsUnique();

        builder.HasOne(d => d.Company)
            .WithMany(c => c.Deals)
            .HasForeignKey(d => d.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<DealStage>>()
            .WithMany()
            .HasForeignKey(d => d.Stage)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<DealStatus>>()
            .WithMany()
            .HasForeignKey(d => d.O2CStatus)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DealLineItemConfiguration : IEntityTypeConfiguration<DealLineItem>
{
    public void Configure(EntityTypeBuilder<DealLineItem> builder)
    {
        builder.ToTable("DealLineItem", t =>
        {
            t.HasCheckConstraint("CK_DealLineItem_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_DealLineItem_UnitPrice", "[UnitPrice] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Sku).HasMaxLength(50).IsUnicode(false);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 2);

        builder.HasOne(l => l.Deal)
            .WithMany(d => d.LineItems)
            .HasForeignKey(l => l.DealId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DealNoteConfiguration : IEntityTypeConfiguration<DealNote>
{
    public void Configure(EntityTypeBuilder<DealNote> builder)
    {
        builder.ToTable("DealNote");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Status).HasColumnName("DealStatusId").HasConversion<byte>();
        builder.Property(n => n.ErpOrderNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(n => n.Note).HasMaxLength(1000);

        builder.HasOne(n => n.Deal)
            .WithMany(d => d.Notes)
            .HasForeignKey(n => n.DealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<DealStatus>>()
            .WithMany()
            .HasForeignKey(n => n.Status)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
