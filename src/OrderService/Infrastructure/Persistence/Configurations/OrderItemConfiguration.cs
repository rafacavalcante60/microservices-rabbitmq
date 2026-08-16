using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain;

namespace OrderService.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");

        builder.Property(item => item.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(item => item.ProductName).HasColumnName("product_name").IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("quantity").IsRequired();

        builder.Property(item => item.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        // LineTotal é calculado no domínio. Persistir daria uma segunda fonte
        // de verdade para o mesmo número, livre para divergir da primeira.
        builder.Ignore(item => item.LineTotal);
    }
}
