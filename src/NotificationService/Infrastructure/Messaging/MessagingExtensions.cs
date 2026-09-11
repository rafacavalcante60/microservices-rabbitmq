using BuildingBlocks.Messaging;
using MassTransit;
using NotificationService.Application.Consumers;

namespace NotificationService.Infrastructure.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddNotificationsMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRedisIdempotency(configuration.GetConnectionString("Redis")!);

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<PaymentApprovedConsumer>();
            bus.AddConsumer<PaymentDeclinedConsumer>();
            bus.AddConsumer<PaymentFailedConsumer>();

            // Sem outbox aqui, e isso é deliberado: este serviço não publica
            // evento nenhum. O outbox protege a atomicidade entre escrever no
            // banco e publicar; sem publicação, não há o que proteger. Ligá-lo
            // por simetria criaria três tabelas ociosas e um processo de
            // varredura sem função.
            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(configuration.GetConnectionString("RabbitMq")!));

                // Princípio IV. A chave no Redis inclui o nome da fila, então o
                // mesmo PaymentApproved consumido aqui e no OrderService usa
                // chaves diferentes — sem isso, quem chegasse depois descartaria
                // um evento que nunca viu, e a notificação simplesmente não
                // existiria.
                rabbit.UseIdempotency(context);

                // D-7. Retry de infraestrutura: o Mongo piscou. O duplicate key
                // **não** passa por aqui — ele já foi tratado como sucesso no
                // NotificationStore. O que chega ao retry é falha de verdade, e
                // o que sobrevive a três tentativas vai para a `_error` com log
                // (princípio VI, R-6).
                rabbit.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromSeconds(2)));

                // D-15. O prefixo é o que dá a cada serviço a **sua** fila.
                // Sem ele o MassTransit nomeia a fila só pelo tipo da mensagem,
                // e dois serviços que assinam o mesmo evento acabam na mesma
                // fila — viram competing consumers e cada evento chega a só um
                // deles. O exchange continua um só; o que muda é ter duas filas
                // ligadas a ele em vez de uma disputada.
                rabbit.ConfigureEndpoints(
                    context, new KebabCaseEndpointNameFormatter(prefix: "notifications", includeNamespace: false));
            });
        });

        return services;
    }
}
