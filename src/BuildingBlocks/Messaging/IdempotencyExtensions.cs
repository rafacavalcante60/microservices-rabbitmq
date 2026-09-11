using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace BuildingBlocks.Messaging;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddRedisIdempotency(
        this IServiceCollection services,
        string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);

        // Sem esta linha, o ConnectionMultiplexer lança se o Redis não estiver
        // de pé no momento da conexão — e o serviço inteiro não subiria por
        // causa do cache. Com ela, a conexão é tentada em segundo plano e as
        // operações falham individualmente, que é o que o filtro sabe tratar
        // (R-4, falha aberta). Um detalhe pequeno que decide entre "degradado"
        // e "fora do ar".
        options.AbortOnConnectFail = false;

        // Timeouts curtos, e não os 5s padrão: quando o Redis está fora, cada
        // mensagem espera este tempo antes de o filtro decidir seguir sem a
        // checagem (R-4). Cinco segundos por mensagem transformariam "degradado"
        // em fila represada — a falha aberta só cumpre o propósito se falhar
        // rápido. Meio segundo é folgado para um Redis que está no ar: no mesmo
        // compose ele responde em poucos milissegundos. Fora do ar, o custo por
        // mensagem fica em torno de 1s (conexão + comando), contra 10s no
        // padrão da biblioteca.
        options.ConnectTimeout = 500;
        options.AsyncTimeout = 500;
        options.SyncTimeout = 500;

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        services.TryAddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

        return services;
    }

    // Aplica o filtro a **todo** consumidor do barramento. O tipo aberto
    // `IdempotencyFilter<>` é fechado pelo MassTransit para cada tipo de
    // mensagem; registrar um a um seria a mesma regra dependendo de alguém
    // lembrar dela.
    public static void UseIdempotency(this IConsumePipeConfigurator configurator, IRegistrationContext context)
    {
        configurator.UseConsumeFilter(typeof(IdempotencyFilter<>), context);
    }
}
