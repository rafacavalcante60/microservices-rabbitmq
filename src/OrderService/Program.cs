using BuildingBlocks.Health;
using BuildingBlocks.Observability;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OrderService.Api.Endpoints;
using OrderService.Application.CreateOrder;
using OrderService.Application.GetOrder;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseJsonLogging();

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddOrdersMessaging(builder.Configuration);

// Princípio VI. O check do RabbitMQ não aparece aqui porque o MassTransit já
// registra o seu (`masstransit-bus`) ao configurar o barramento, com a tag
// `ready`. Somar um ping próprio ao broker testaria uma conexão diferente da
// que o serviço realmente usa — e um health check que observa outra coisa que
// não o caminho de produção é pior que nenhum.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>("postgres", tags: [HealthCheckExtensions.ReadyTag])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: [HealthCheckExtensions.ReadyTag]);

// Faz o ASP.NET responder em ProblemDetails também nos erros que ele mesmo
// gera — corpo JSON malformado, por exemplo. Sem isto, a resposta de validação
// teria um formato e a de corpo inválido teria outro.
builder.Services.AddProblemDetails();

builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<GetOrderHandler>();

var app = builder.Build();

// Primeiro de tudo no pipeline: qualquer log escrito depois daqui — inclusive
// os de erro — já sai carimbado com o CorrelationId. Registrar mais abaixo
// deixaria um trecho inicial da requisição sem correlação, que é justamente
// onde as falhas de borda acontecem.
app.UseCorrelationId();

// Uma linha de log por requisição, com rota, status e duração, em vez das três
// que o ASP.NET emite por padrão em texto.
app.UseSerilogRequestLogging();

// D-14: a migration é aplicada no startup para que `docker compose up` baste.
// Um passo manual de migration quebraria o "clone e roda" que é critério de
// sucesso da constituição. Em produção com várias réplicas isto seria um job
// separado — várias instâncias migrando o mesmo banco ao mesmo tempo é corrida.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
}

app.MapGet("/", () => "OrderService");

app.MapDefaultHealthChecks();
app.MapOrdersEndpoints();

app.Run();
