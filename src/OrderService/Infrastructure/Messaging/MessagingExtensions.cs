using MassTransit;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Messaging;

public static class MessagingExtensions
{
    // Princípio V / D-3. O problema que o outbox resolve: salvar o pedido no
    // PostgreSQL e publicar OrderCreated no RabbitMQ são escritas em dois
    // sistemas diferentes, e não existe transação que abranja os dois. Se o
    // processo morre entre o commit e a publicação, o pedido existe e ninguém
    // nunca vai cobrá-lo — inconsistência permanente e silenciosa, do tipo que
    // só aparece quando o cliente reclama.
    //
    // A saída é não publicar direto. O evento é gravado como uma linha na
    // tabela outbox_message, **dentro da mesma transação** do pedido: ou os
    // dois existem, ou nenhum. Um serviço de segundo plano (o delivery service)
    // lê a tabela, publica no broker e só então apaga a linha. Se ele morrer no
    // meio, a linha continua lá e a publicação é retentada — o preço é que a
    // entrega passa a ser *at-least-once*, o que joga a responsabilidade da
    // idempotência para o consumidor (princípio IV).
    public static IServiceCollection AddOrdersMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMassTransit(bus =>
        {
            bus.AddEntityFrameworkOutbox<OrdersDbContext>(outbox =>
            {
                outbox.UsePostgres();

                // Sem esta linha o IPublishEndpoint publicaria direto no broker
                // e todo o resto seria decoração. É ela que redireciona a
                // publicação para a tabela, dentro da transação do SaveChanges.
                outbox.UseBusOutbox();

                // Intervalo de varredura da tabela. O padrão da biblioteca é
                // conservador demais para uma demonstração: um segundo entre
                // criar o pedido e ver o evento na fila é a diferença entre o
                // fluxo parecer vivo e parecer travado.
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(configuration.GetConnectionString("RabbitMq")!));
                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
