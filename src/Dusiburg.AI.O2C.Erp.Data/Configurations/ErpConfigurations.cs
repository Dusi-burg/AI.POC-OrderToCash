using Dusiburg.AI.O2C.Erp.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dusiburg.AI.O2C.Erp.Data.Configurations;

// Convenzioni (D29-D31): tabelle al singolare, PK numerica "Id", FK qualificate (<Entity>Id), niente PK GUID (colonna dedicata univoca),
// chiavi di business stringa come colonne univoche, enum come FK verso tabelle di lookup tinyint generate dal codice.
// Codici e chiavi in varchar, testi liberi in nvarchar.

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

internal sealed class OrderStatusConfiguration() : EnumLookupConfiguration<OrderStatus>("OrderStatus");

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customer", t => t.HasCheckConstraint("CK_Customer_CreditLimit", "[CreditLimit] >= 0"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.VatNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(254).IsUnicode(false);
        builder.Property(c => c.Address).HasMaxLength(400);
        builder.Property(c => c.CreditLimit).HasPrecision(18, 2);

        builder.HasIndex(c => c.VatNumber).IsUnique().HasFilter("[VatNumber] IS NOT NULL");
        builder.HasIndex(c => c.Email);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product", t => t.HasCheckConstraint("CK_Product_ListPrice", "[ListPrice] >= 0"));

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).HasMaxLength(50).IsUnicode(false);
        builder.Property(p => p.Description).HasMaxLength(200);
        builder.Property(p => p.ListPrice).HasPrecision(18, 2);
        builder.Property(p => p.Uom).HasMaxLength(10).IsUnicode(false);

        builder.HasIndex(p => p.Sku).IsUnique();
    }
}

internal sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("StockLevel", t =>
        {
            t.HasCheckConstraint("CK_StockLevel_OnHand", "[OnHand] >= 0");
            t.HasCheckConstraint("CK_StockLevel_Reserved", "[Reserved] >= 0");
            t.HasCheckConstraint("CK_StockLevel_LeadTimeDays", "[LeadTimeDays] >= 0");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RowVersion).IsRowVersion();

        // 1:1 con Product: l'indice univoco su ProductId garantisce una sola giacenza per prodotto.
        builder.HasOne(s => s.Product)
            .WithOne(p => p.StockLevel)
            .HasForeignKey<StockLevel>(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Order", t => t.HasCheckConstraint("CK_Order_Total", "[Total] >= 0"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.PublicId).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.Total).HasPrecision(18, 2);
        builder.Property(o => o.Status).HasColumnName("OrderStatusId").HasConversion<byte>();
        builder.Property(o => o.ExternalRef).HasMaxLength(50).IsUnicode(false);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100).IsUnicode(false);

        builder.HasIndex(o => o.PublicId).IsUnique();
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();
        builder.HasIndex(o => o.ExternalRef);

        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<OrderStatus>>()
            .WithMany()
            .HasForeignKey(o => o.Status)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLine", t =>
        {
            t.HasCheckConstraint("CK_OrderLine_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderLine_UnitPrice", "[UnitPrice] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.UnitPrice).HasPrecision(18, 2);

        builder.HasOne(l => l.Order)
            .WithMany(o => o.Lines)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
