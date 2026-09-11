using BuildingBlocks.Messaging;
using MassTransit;
using OrderService.Application.Consumers;
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
        services.AddRedisIdempotency(configuration.GetConnectionString("Redis")!);

        services.AddMassTransit(bus =>
        {
            // T-018. Os três desfechos chegam em contratos separados, então são
            // três consumidores e três filas — cada um falha, retenta e vai
            // para a DLQ sem arrastar os outros.
            bus.AddConsumer<PaymentApprovedConsumer>();
            bus.AddConsumer<PaymentDeclinedConsumer>();
            bus.AddConsumer<PaymentFailedConsumer>();

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

                // Princípio IV: aplicado ao pipeline inteiro, antes de qualquer
                // consumidor. O mesmo desfecho reentregue não transiciona duas
                // vezes — e mesmo que o Redis esteja fora (R-4, falha aberta), a
                // guarda de estado no agregado segura a correção.
                rabbit.UseIdempotency(context);

                // D-7. Retry de **infraestrutura** — o Postgres piscou, a
                // conexão caiu. O que falhar nas três tentativas vai para a
                // fila `_error` com log, nunca é descartado (princípio VI).
                rabbit.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(2)));

                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
