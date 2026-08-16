using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain;

namespace OrderService.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).HasColumnName("id");

        builder.Property(order => order.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(order => order.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .IsRequired();

        // numeric(18,2), nunca float: ponto flutuante binário não representa
        // 0,10 exatamente, e erro de arredondamento em dinheiro é defeito.
        builder.Property(order => order.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(order => order.Currency)
            .HasColumnName("currency")
            .HasColumnType("char(3)")
            .HasDefaultValue(Order.DefaultCurrency)
            .IsRequired();

        // D-10: o estado vai como texto. Um int no banco torna o suporte cego —
        // ninguém sabe se 2 é Pago ou Recusado sem abrir o código, e reordenar
        // o enum corromperia o histórico em silêncio.
        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(order => order.StatusReason)
            .HasColumnName("status_reason");

        builder.Property(order => order.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(order => order.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // RN-8 / CA-11 / CA-12: a garantia de idempotência é esta linha. Não é
        // uma checagem em código que uma corrida entre duas requisições pode
        // furar — é o banco recusando a segunda inserção.
        builder.HasIndex(order => new { order.CustomerId, order.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_orders_customer_id_idempotency_key");

        builder.HasIndex(order => order.Status).HasDatabaseName("ix_orders_status");

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey("order_id")
            .IsRequired() // item órfão não existe: ou pertence a um pedido, ou não é um item
            .OnDelete(DeleteBehavior.Cascade);

        // Os itens entram pela lista privada, não pela propriedade pública de
        // leitura: o encapsulamento do agregado continua valendo com o EF junto.
        builder.Metadata
            .FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
