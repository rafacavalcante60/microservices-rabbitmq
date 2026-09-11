using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentService.Domain;

namespace PaymentService.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    // Mesma razão da constante equivalente em OrderConfiguration: o consumidor
    // da T-017 vai reconhecer *esta* violação de unicidade pelo nome para
    // tratá-la como cobrança repetida, e não como erro. Nome solto em duas
    // pontas é o tipo de acoplamento que quebra em silêncio.
    public const string UniqueOrderIndexName = "ix_payments_order_id";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).HasColumnName("id");

        builder.Property(payment => payment.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(payment => payment.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        // D-10: texto, não int. Vale aqui o mesmo que em `orders` — quem abre o
        // banco no suporte lê "Declined", não "1".
        builder.Property(payment => payment.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(payment => payment.Reason).HasColumnName("reason");

        builder.Property(payment => payment.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(payment => payment.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(payment => payment.CompletedAt).HasColumnName("completed_at").IsRequired();

        // RN-9 / CA-10: **a** garantia de que um pedido é cobrado uma vez só.
        //
        // A checagem de idempotência no Redis (T-016) evita o trabalho; esta
        // linha impede o dano. A redundância é deliberada: a primeira é
        // otimização e pode falhar aberta quando o Redis cai (R-4); a segunda é
        // correção e não depende de nada além do próprio Postgres. Duas
        // instâncias do consumidor processando a mesma mensagem ao mesmo tempo
        // passam pelo Redis; não passam por aqui.
        builder.HasIndex(payment => payment.OrderId)
            .IsUnique()
            .HasDatabaseName(UniqueOrderIndexName);
    }
}
