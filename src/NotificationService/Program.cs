using BuildingBlocks.Health;
using BuildingBlocks.Observability;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseJsonLogging();

builder.Services.AddNotificationsMongo(builder.Configuration);
builder.Services.AddNotificationsMessaging(builder.Configuration);

// Princípio VI. O check do RabbitMQ vem do MassTransit (`masstransit-bus`), como
// nos outros serviços.
builder.Services.AddHealthChecks()
    .AddCheck<MongoHealthCheck>("mongodb", tags: [HealthCheckExtensions.ReadyTag])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: [HealthCheckExtensions.ReadyTag]);

var app = builder.Build();

app.UseCorrelationId();
app.UseSerilogRequestLogging();

// O equivalente à migration dos outros dois serviços: o Mongo cria coleção sob
// demanda, mas o índice único precisa existir **antes** da primeira inserção —
// criá-lo depois falharia se duplicatas já tivessem entrado. D-14 vale aqui
// pelo mesmo motivo: `docker compose up` tem que bastar.
await app.Services.EnsureNotificationIndexesAsync();

app.MapGet("/", () => "NotificationService");

app.MapDefaultHealthChecks();

app.Run();
