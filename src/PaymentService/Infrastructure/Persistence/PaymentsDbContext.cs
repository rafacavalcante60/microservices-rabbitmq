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

        base.OnModelCreating(modelBuilder);
    }
}
