using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OrderService.Api.Endpoints;
using OrderService.Application.CreateOrder;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddOrdersMessaging(builder.Configuration);

// Faz o ASP.NET responder em ProblemDetails também nos erros que ele mesmo
// gera — corpo JSON malformado, por exemplo. Sem isto, a resposta de validação
// teria um formato e a de corpo inválido teria outro.
builder.Services.AddProblemDetails();

builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();
builder.Services.AddScoped<CreateOrderHandler>();

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

app.MapOrdersEndpoints();

app.Run();
