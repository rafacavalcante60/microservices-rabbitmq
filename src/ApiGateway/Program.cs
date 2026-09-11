using BuildingBlocks.Health;
using BuildingBlocks.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseJsonLogging();

// D-12. A configuração do YARP é declarativa: rotas e clusters vivem no
// appsettings, não em código. Mudar para onde `/orders/**` aponta é editar
// JSON — ou uma variável de ambiente no compose (T-022) — sem recompilar.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// O gateway não tem dependência para checar: ele não fala com banco, broker
// nem cache. `/health/ready` existe mesmo assim, sem nenhum check registrado,
// porque o orquestrador pergunta a mesma coisa a todos os serviços e um que
// responde 404 para a pergunta padrão é um serviço que ninguém consegue operar.
//
// Deliberadamente **não** se checa se OrderService está de pé: o gateway estar
// pronto significa "eu consigo rotear", não "todos os meus destinos estão
// saudáveis". Amarrar os dois faria a queda de um serviço tirar a porta de
// entrada inteira do ar, inclusive para as rotas que continuam funcionando.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Princípio VII, e este é **o** motivo do gateway existir além do roteamento.
// Aqui é a borda: se a requisição não trouxe X-Correlation-Id, é neste ponto
// que o identificador nasce. Daqui ele segue no header para o OrderService, de
// lá entra no evento OrderCreated, e atravessa PaymentService e
// NotificationService pelas mensagens. Um fio só para os três saltos.
app.UseCorrelationId();

app.UseSerilogRequestLogging();

app.MapGet("/", () => "ApiGateway");

app.MapDefaultHealthChecks();

// Depois dos endpoints próprios: o que o gateway atende, ele atende; o que não
// reconhece, o proxy encaminha.
app.MapReverseProxy();

app.Run();
