using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain;

namespace PaymentService.Infrastructure.Persistence;

// Princípio III: este contexto aponta para `payments_db`, e só. Não existe
// DbSet<Order> aqui nem consulta ao banco de pedidos — o que o PaymentService
// sabe sobre um pedido é o que chegou no evento. A separação não é convenção
// amigável: o `payments_user` do init.sql nem consegue conectar em `orders_db`.
public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);

        // Princípio V, do lado de cá. O PaymentService tem o mesmo problema que
        // o OrderService tinha: gravar a linha em `payments` e publicar o
        // desfecho são duas escritas em sistemas diferentes. Se a segunda
        // falhasse depois da primeira, o cliente teria sido cobrado e ninguém
        // saberia — nem o pedido mudaria de estado, nem a notificação sairia.
        //
        // Com as tabelas aqui, o evento é gravado na mesma transação do
        // pagamento: ou os dois existem, ou nenhum.
        //
        // inbox_state entra junto porque este serviço também **consome**
        // (OrderCreated). Ela não é usada pela checagem de idempotência que
        // escolhemos (D-4, Redis), mas o MassTransit a exige no mesmo contexto
        // quando o outbox está configurado.
        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_message"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));

        base.OnModelCreating(modelBuilder);
    }
}
