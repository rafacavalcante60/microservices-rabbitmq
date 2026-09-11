using Microsoft.EntityFrameworkCore;
using PaymentService.Application.ChargeOrder;
using PaymentService.Infrastructure;
using PaymentService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentsDb")));

builder.Services.AddSimulatedPaymentGateway(builder.Configuration);

builder.Services.Configure<PaymentRetryOptions>(
    builder.Configuration.GetSection(PaymentRetryOptions.SectionName));
builder.Services.AddScoped<ChargeOrderHandler>();

var app = builder.Build();

// D-14: migration no startup, pelo mesmo motivo do OrderService — um
// `docker compose up` precisa bastar. Em produção com várias réplicas isto
// seria um job separado; várias instâncias migrando o mesmo banco é corrida.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
}

app.MapGet("/", () => "Hello World!");

app.Run();
