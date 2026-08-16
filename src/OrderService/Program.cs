using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddOrdersMessaging(builder.Configuration);

var app = builder.Build();

// D-14: a migration é aplicada no startup para que `docker compose up` baste.
// Um passo manual de migration quebraria o "clone e roda" que é critério de
// sucesso da constituição. Em produção com várias réplicas isto seria um job
// separado — várias instâncias migrando o mesmo banco ao mesmo tempo é corrida.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
}

app.MapGet("/", () => "OrderService");

app.Run();
