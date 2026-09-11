using BuildingBlocks.Health;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.ChargeOrder;
using PaymentService.Infrastructure;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseJsonLogging();

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentsDb")));

builder.Services.AddPaymentsMessaging(builder.Configuration);

builder.Services.AddSimulatedPaymentGateway(builder.Configuration);

builder.Services.Configure<PaymentRetryOptions>(
    builder.Configuration.GetSection(PaymentRetryOptions.SectionName));
builder.Services.AddScoped<ChargeOrderHandler>();

// Princípio VI. O check do RabbitMQ vem de graça com o MassTransit
// (`masstransit-bus`, com a tag `ready`), como no OrderService.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentsDbContext>("postgres", tags: [HealthCheckExtensions.ReadyTag])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: [HealthCheckExtensions.ReadyTag]);

var app = builder.Build();

app.UseCorrelationId();
app.UseSerilogRequestLogging();

// D-14: migration no startup, pelo mesmo motivo do OrderService — um
// `docker compose up` precisa bastar. Em produção com várias réplicas isto
// seria um job separado; várias instâncias migrando o mesmo banco é corrida.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
}

app.MapGet("/", () => "PaymentService");

app.MapDefaultHealthChecks();

app.Run();
