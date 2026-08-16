using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain;

namespace OrderService.Infrastructure.Persistence;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);

        // As três tabelas do outbox do MassTransit vivem neste mesmo DbContext —
        // e é justamente isso que permite gravar o pedido e o evento na mesma
        // transação. Se estivessem em outro contexto ou em outro banco, o
        // padrão perderia o sentido.
        //
        // outbox_message: os eventos aguardando publicação.
        // outbox_state:   o controle de qual lote já foi entregue ao broker.
        // inbox_state:    a contrapartida na recepção, usada quando o serviço
        //                 consumir eventos (T-018).
        //
        // Só os nomes de tabela são ajustados para o snake_case do restante do
        // esquema. As colunas ficam como a biblioteca as define: são estrutura
        // interna dela, não modelo nosso, e renomeá-las seria manutenção sem
        // contrapartida a cada atualização do pacote.
        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_message"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));

        base.OnModelCreating(modelBuilder);
    }
}
