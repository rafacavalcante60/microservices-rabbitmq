using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace NotificationService.Infrastructure.Persistence;

public static class MongoExtensions
{
    public static IServiceCollection AddNotificationsMongo(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));

        // Singleton porque o MongoClient já gerencia o próprio pool de conexões
        // internamente. Criar um por requisição — erro comum com este driver —
        // abriria um pool novo a cada vez e esgotaria os sockets do servidor.
        services.AddSingleton<IMongoClient>(provider =>
            new MongoClient(provider.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

            return provider.GetRequiredService<IMongoClient>()
                .GetDatabase(options.Database)
                .GetCollection<Notification>(options.Collection);
        });

        services.AddSingleton<NotificationStore>();

        return services;
    }

    // RN-12. O índice único em `orderId` é a garantia final de "exatamente uma
    // notificação por pedido" — o equivalente aqui à constraint única em
    // `payments.order_id` do PaymentService. Ele não é otimização de consulta:
    // é regra de negócio aplicada pelo armazenamento, o único lugar que nenhum
    // caminho de código consegue contornar.
    //
    // `CreateOneAsync` é idempotente para um índice com a mesma definição e o
    // mesmo nome, então rodar a cada startup não acumula nada.
    public static async Task EnsureNotificationIndexesAsync(this IServiceProvider services)
    {
        var collection = services.GetRequiredService<IMongoCollection<Notification>>();

        await collection.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(notification => notification.OrderId),
                new CreateIndexOptions { Unique = true, Name = "ux_notifications_orderId" }),

            // Histórico por cliente (plan.md §NotificationService). Não é único:
            // um cliente tem muitos pedidos.
            new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(notification => notification.CustomerId),
                new CreateIndexOptions { Name = "ix_notifications_customerId" })
        ]);
    }
}

// Um ping próprio em vez do pacote AspNetCore.HealthChecks.MongoDb: o check usa
// o **mesmo** IMongoClient que os consumidores usam, e não uma conexão paralela
// criada só para o teste. Um health check que observa outra coisa que não o
// caminho de produção mente exatamente quando se precisa dele.
public class MongoHealthCheck(IMongoClient client, IOptions<MongoOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.GetDatabase(options.Value.Database)
                .RunCommandAsync<MongoDB.Bson.BsonDocument>("{ ping: 1 }", cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MongoDB não respondeu ao ping.", exception);
        }
    }
}
