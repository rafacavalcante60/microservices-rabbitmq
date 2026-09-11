using System.Net;
using System.Text.Json;
using Integration.Tests.Fixtures;

namespace Integration.Tests;

// O teste que prova que a base montada na T-023 funciona: os três serviços sobem
// contra infraestrutura real e cada um responde que alcança as suas dependências.
// Se este ficar vermelho, nenhum teste de fluxo das tarefas seguintes significa
// coisa alguma — a falha seria do andaime, não do sistema.
[Collection(IntegrationCollection.Name)]
public class SmokeTests(IntegrationFixture fixture)
{
    public static TheoryData<string> ServiceNames => new() { "orders", "payments", "notifications" };

    [Theory]
    [MemberData(nameof(ServiceNames))]
    public async Task Health_ready_fica_verde_em_todos_os_servicos(string service)
    {
        using var client = CreateClient(service);

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);

        // O corpo é lido em vez de só o status porque o /health/ready devolve o
        // estado de cada dependência: quando ele fica vermelho, a pergunta útil
        // não é "falhou?" e sim "qual delas".
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        json.RootElement.GetProperty("checks").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(ServiceNames))]
    public async Task Health_responde_sem_tocar_nas_dependencias(string service)
    {
        using var client = CreateClient(service);

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateClient(string service) => service switch
    {
        "orders" => fixture.Orders.CreateClient(),
        "payments" => fixture.Payments.CreateClient(),
        "notifications" => fixture.Notifications.CreateClient(),
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, "serviço desconhecido")
    };
}
