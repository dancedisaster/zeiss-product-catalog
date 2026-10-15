using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            // Defence in depth: the database itself refuses invalid states.
            table.HasCheckConstraint("CK_Products_Id_SixDigits", $"[Id] BETWEEN {Product.MinId} AND {Product.MaxId}");
            table.HasCheckConstraint("CK_Products_Stock_NonNegative", "[Stock] >= 0");
            table.HasCheckConstraint("CK_Products_Price_Positive", "[Price] > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(p => p.Sku).HasMaxLength(Product.SkuMaxLength).IsRequired();
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.Version).IsConcurrencyToken();

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Stock);

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
