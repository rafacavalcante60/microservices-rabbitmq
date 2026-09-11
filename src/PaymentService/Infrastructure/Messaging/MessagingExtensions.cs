using BuildingBlocks.Messaging;
using MassTransit;
using PaymentService.Application.Consumers;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddPaymentsMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRedisIdempotency(configuration.GetConnectionString("Redis")!);

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<OrderCreatedConsumer>();

            bus.AddEntityFrameworkOutbox<PaymentsDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(configuration.GetConnectionString("RabbitMq")!));

                // Princípio IV: aplicado ao pipeline inteiro, antes de qualquer
                // consumidor. Nenhum consumidor precisa lembrar de se proteger.
                rabbit.UseIdempotency(context);

                // D-7. **Este** retry é o de infraestrutura: o Postgres caiu, a
                // conexão piscou. Nada a ver com o retry do Polly na chamada ao
                // gateway (D-6), que trata desfecho de negócio.
                //
                // Três tentativas com intervalo curto porque falha de infra ou
                // passa rápido ou não passa: insistir mais só segura a fila. O
                // que falhar nas três vai para a fila `_error` — a DLQ do
                // MassTransit — com log, nunca descartado em silêncio
                // (princípio VI).
                rabbit.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(2)));

                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
