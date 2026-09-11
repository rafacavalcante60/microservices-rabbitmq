using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NotificationService;
using OrderService;
using PaymentService;
using PaymentService.Domain;
using PaymentService.Infrastructure;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Integration.Tests.Fixtures;

// Princípio IX. Sobe RabbitMQ, PostgreSQL, MongoDB e Redis de verdade em
// contêineres descartáveis e liga os três serviços neles.
//
// Sem mock de broker: o que costuma quebrar em sistema de mensageria não é a
// lógica do consumidor, é a serialização do contrato, o nome da fila e o
// roteamento do exchange — exatamente o que um broker falso não exercita. Um
// teste verde contra um broker em memória e vermelho contra o RabbitMQ é o pior
// resultado possível, porque só aparece em produção.
//
// Os contêineres sobem uma vez por execução (ver IntegrationCollection), não por
// teste: são ~15s de startup que não fazem sentido pagar em cada caso.
public sealed class IntegrationFixture : IAsyncLifetime
{
    private const string PostgresUser = "postgres";
    private const string PostgresPassword = "postgres";

    // As mesmas imagens do docker-compose.yml. Testar contra outra versão
    // significaria que o verde aqui não diz nada sobre o que `docker compose up`
    // sobe na máquina de quem clonou o repositório.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithUsername(PostgresUser)
        .WithPassword(PostgresPassword)
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:7")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public WebApplicationFactory<OrderServiceEntryPoint> Orders { get; private set; } = null!;

    public WebApplicationFactory<PaymentServiceEntryPoint> Payments { get; private set; } = null!;

    public WebApplicationFactory<NotificationServiceEntryPoint> Notifications { get; private set; } = null!;

    public HttpClient OrdersClient { get; private set; } = null!;

    public HttpClient NotificationsClient { get; private set; } = null!;

    // O dublê que T-026 usa para derrubar o gateway de um pedido só. Sem
    // roteiro escrito, ele delega ao simulador de verdade — ver
    // ScriptedPaymentGateway.
    public ScriptedPaymentGateway Gateway => (ScriptedPaymentGateway)Payments.Services
        .GetRequiredService<IPaymentGateway>();

    public string MongoConnectionString => _mongo.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Em paralelo porque são independentes: quatro startups em série somam
        // perto de um minuto, e o tempo de suíte é o que decide se ela é rodada.
        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync(),
            _mongo.StartAsync(),
            _redis.StartAsync());

        // Princípio III: um banco por serviço, nunca schema compartilhado. O
        // contêiner é um só — dois processos de Postgres não provariam nada além
        // de gastar memória —, mas os bancos são separados e cada serviço só
        // conhece o seu.
        await _postgres.ExecScriptAsync("CREATE DATABASE orders_db; CREATE DATABASE payments_db;");

        ApplyConfiguration();

        Orders = new WebApplicationFactory<OrderServiceEntryPoint>();

        // O registro entra depois do Program.cs, e o último a registrar
        // IPaymentGateway é quem responde — o simulador continua no contêiner
        // como tipo concreto, para o dublê poder delegar a ele.
        Payments = new WebApplicationFactory<PaymentServiceEntryPoint>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<SimulatedPaymentGateway>();
                services.AddSingleton<IPaymentGateway, ScriptedPaymentGateway>();
            }));
        Notifications = new WebApplicationFactory<NotificationServiceEntryPoint>();

        // O host de cada serviço só é construído no primeiro CreateClient, e é aí
        // que migration, índice do Mongo e consumidores entram no ar. Criar os
        // três clientes agora, e não dentro do primeiro teste, garante que todo
        // teste comece com o barramento inteiro escutando — senão o primeiro
        // evento publicado pode não ter ninguém do outro lado ainda.
        OrdersClient = Orders.CreateClient();
        NotificationsClient = Notifications.CreateClient();
        Payments.CreateClient().Dispose();
    }

    // Por variável de ambiente, e não por `ConfigureAppConfiguration`.
    //
    // Não é preferência: os serviços leem parte da configuração **na hora do
    // registro**, ainda dentro do Program.cs — `AddRedis(...GetConnectionString
    // ("Redis"))` é um desses. As fontes que o WebApplicationFactory injeta só
    // entram quando o host é construído, depois disso, então o health check do
    // Redis continuaria apontando para o localhost do appsettings enquanto
    // Postgres e RabbitMQ (lidos dentro de lambdas, tardiamente) já estariam nos
    // contêineres. Variável de ambiente é lida pelo CreateBuilder antes de
    // qualquer linha do Program, o que cobre os dois casos — e é literalmente o
    // mesmo mecanismo que o docker-compose.yml usa, com o mesmo `__` de
    // separador. As chaves não colidem entre os três serviços: cada banco tem a
    // sua, e broker e cache são de fato compartilhados.
    private void ApplyConfiguration()
    {
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings__OrdersDb"] = PostgresConnectionString("orders_db"),
            ["ConnectionStrings__PaymentsDb"] = PostgresConnectionString("payments_db"),
            ["ConnectionStrings__RabbitMq"] = _rabbitMq.GetConnectionString(),
            ["ConnectionStrings__Redis"] = RedisConnectionString,
            ["Mongo__ConnectionString"] = MongoConnectionString,

            // A política de retry é a mesma da produção; só o relógio encolhe.
            // Com os 500ms padrão, as quatro esperas de CA-13 somam 7,5s de
            // suíte parada esperando backoff que já está coberto por teste de
            // unidade. O que o teste de integração tem a dizer é quantas
            // tentativas acontecem e onde o pedido termina, não quanto tempo se
            // espera entre elas.
            ["PaymentRetry__BaseDelay"] = "00:00:00.020"
        };

        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    // `rabbitmqctl stop_app` derruba o broker mantendo o contêiner e as portas
    // de pé — parar o contêiner sortearia outra porta ao subir de novo, e os
    // serviços ficariam apontando para o vazio. As filas e exchanges são
    // duráveis, então voltam como estavam.
    //
    // É o que permite testar R-1: o POST continua respondendo 201 com o broker
    // fora, porque o evento vai para a tabela outbox, não para a fila.
    public Task StopBrokerAsync() => ExecBrokerAsync("stop_app");

    public Task StartBrokerAsync() => ExecBrokerAsync("start_app");

    private async Task ExecBrokerAsync(string command)
    {
        var result = await _rabbitMq.ExecAsync(["rabbitmqctl", command]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"rabbitmqctl {command} falhou ({result.ExitCode}): {result.Stderr}");
        }
    }

    public async Task DisposeAsync()
    {
        OrdersClient?.Dispose();
        NotificationsClient?.Dispose();

        // Os serviços primeiro: derrubar o RabbitMQ debaixo de um consumidor
        // ativo enche a saída do teste de erro de reconexão sem nenhum motivo.
        await (Orders?.DisposeAsync() ?? ValueTask.CompletedTask);
        await (Payments?.DisposeAsync() ?? ValueTask.CompletedTask);
        await (Notifications?.DisposeAsync() ?? ValueTask.CompletedTask);

        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask(),
            _mongo.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask());
    }

    private string PostgresConnectionString(string database) =>
        $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};" +
        $"Database={database};Username={PostgresUser};Password={PostgresPassword}";
}
