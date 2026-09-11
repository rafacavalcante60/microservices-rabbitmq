using System.Net;
using System.Net.Http.Json;
using Integration.Tests.Fixtures;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.GetNotifications;
using NotificationService.Infrastructure.Persistence;
using OrderService.Application.CreateOrder;
using OrderService.Application.GetOrder;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using Shared.Contracts;

namespace Integration.Tests;

// Os dois modos de falha que a spec previu e o sistema tem de sobreviver a
// ambos — e eles são diferentes em natureza, não em grau:
//
//   O **gateway** fora do ar é falha de terceiro. Não há o que consertar do
//   nosso lado, então ela vira desfecho de negócio: tenta 5 vezes, desiste,
//   marca o pedido e avisa o cliente (RN-11). O sistema segue funcionando.
//
//   O **broker** fora do ar é falha nossa, e aí desfecho nenhum é aceitável:
//   perder um OrderCreated significaria um pedido que ninguém jamais cobraria.
//   O outbox transforma a indisponibilidade em atraso — o evento espera no
//   banco e sai quando o RabbitMQ volta (R-1).
//
// Falha é estado esperado (princípio VI): o que estes testes verificam é que
// ela foi projetada, não que foi evitada.
[Collection(IntegrationCollection.Name)]
public class ResilienceTests(IntegrationFixture fixture)
{
    private const decimal ValorAprovado = 500.00m;

    // RN-11. Repetido aqui de propósito, em vez de lido de PaymentRetryOptions:
    // o teste tem de falhar quando alguém mudar a configuração, e um teste que
    // lê o número da mesma fonte que o código verifica apenas que 5 == 5.
    private const int TentativasEsperadas = 5;

    private static readonly TimeSpan TempoDeConvergencia = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Gateway_que_nunca_responde_leva_o_pedido_a_falha_apos_cinco_tentativas()
    {
        var order = await SemearPedidoAsync();

        // CA-13. O roteiro é escrito antes de o evento existir — daí o pedido
        // ser semeado e o OrderCreated publicado à mão. Pelo POST, o outbox
        // publicaria o evento sozinho em até um segundo, e a cobrança poderia
        // acontecer antes de o teste conseguir derrubar o gateway.
        fixture.Gateway.NuncaResponde(order.Id);

        await PublicarCriacaoAsync(order);

        var pedido = await AguardarStatusAsync(order.Id, "PaymentFailed");

        // Falhou não é recusado, e a diferença precisa chegar ao cliente: o
        // motivo aqui fala de processador que não respondeu, não de limite
        // excedido. Confundir os dois faria o cliente procurar problema no
        // cartão dele por um incidente nosso.
        pedido.StatusReason.Should().NotBeNullOrWhiteSpace();

        // O ponto exato de RN-11. Sem esta linha, o teste passaria igual se o
        // consumidor tentasse cinquenta vezes — e um gateway real cobraria por
        // cada uma delas.
        fixture.Gateway.TentativasDe(order.Id).Should().Be(TentativasEsperadas);

        var notificacoes = await AguardarNotificacoesAsync(order.Id);
        notificacoes.Should().ContainSingle();
        notificacoes.Single().Type.Should().Be(NotificationType.PaymentFailed);
    }

    [Fact]
    public async Task Gateway_que_falha_duas_vezes_e_aprova_na_terceira_paga_o_pedido()
    {
        var order = await SemearPedidoAsync();

        // CA-14. A indisponibilidade passageira é o caso comum de verdade — um
        // timeout de rede, um deploy do outro lado. É para ela que o retry
        // existe; o cenário de cima é o que sobra quando ele não basta.
        fixture.Gateway.FalhaAntesDeResponder(order.Id, falhas: 2);

        await PublicarCriacaoAsync(order);

        await AguardarStatusAsync(order.Id, "Paid");

        fixture.Gateway.TentativasDe(order.Id).Should().Be(3);

        var notificacoes = await AguardarNotificacoesAsync(order.Id);

        // Uma só, e de confirmação. As duas falhas intermediárias são assunto
        // interno: o cliente não tem o que fazer com elas, e avisá-lo de cada
        // retentativa seria transformar ruído de infraestrutura em ansiedade.
        notificacoes.Should().ContainSingle();
        notificacoes.Single().Type.Should().Be(NotificationType.OrderPaid);
    }

    [Fact]
    public async Task Pedido_criado_com_o_broker_fora_completa_o_fluxo_quando_ele_volta()
    {
        // R-1, e a razão de o outbox existir (princípio V). Aqui o pedido entra
        // pelo POST de verdade: é justamente o caminho em que salvar no banco e
        // publicar no broker são duas escritas em sistemas diferentes, e o que
        // se quer provar é que a segunda não pode se perder.
        await fixture.StopBrokerAsync();

        Guid orderId;

        try
        {
            var (status, body) = await CriarPelaApiAsync();

            // O 201 com o broker no chão é o resultado que resume a tarefa: a
            // criação não depende da mensageria estar de pé. Sem outbox, este
            // POST ou falharia — derrubando a loja junto com o broker — ou
            // responderia 201 e perderia o evento, que é pior, porque ninguém
            // perceberia.
            status.Should().Be(HttpStatusCode.Created);
            orderId = body.OrderId;

            var pendente = await ConsultarAsync(orderId);
            pendente!.Status.Should().Be("Pending", "sem broker, ninguém cobrou ainda");
        }
        finally
        {
            // No finally porque um assert que falha acima não pode deixar o
            // broker desligado para as outras classes da coleção — o fixture é
            // compartilhado, e o estrago seria uma cascata de falhas sem
            // relação com a causa.
            await fixture.StartBrokerAsync();
        }

        // Janela maior que a das outras esperas: além do fluxo normal, o
        // MassTransit precisa reconectar ao broker que voltou e o publicador do
        // outbox precisa pegar a linha represada na varredura seguinte.
        var reconexao = TimeSpan.FromSeconds(90);

        var pedido = await AguardarStatusAsync(orderId, "Paid", reconexao);
        pedido.Status.Should().Be("Paid");

        var notificacoes = await AguardarNotificacoesAsync(orderId, reconexao);
        notificacoes.Should().ContainSingle();
        notificacoes.Single().Type.Should().Be(NotificationType.OrderPaid);
    }

    private async Task<Order> SemearPedidoAsync()
    {
        var order = new Order(
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            [new OrderItem(Guid.NewGuid(), "Cafeteira", 1, ValorAprovado)]);

        using var scope = fixture.Orders.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order;
    }

    // Pelo IBus da raiz: o IPublishEndpoint de um escopo gravaria no outbox, e
    // o que estes dois primeiros testes querem é o evento na fila agora, com o
    // roteiro do gateway já escrito.
    private async Task PublicarCriacaoAsync(Order order)
    {
        var bus = fixture.Orders.Services.GetRequiredService<IBus>();

        await bus.Publish(new OrderCreated(
            order.Id, order.CustomerId, order.TotalAmount, order.Currency,
            DateTime.UtcNow, CorrelationId: Guid.NewGuid()));
    }

    private async Task<(HttpStatusCode Status, CreateOrderResponse Body)> CriarPelaApiAsync()
    {
        var request = new CreateOrderRequest(
            CustomerId: Guid.NewGuid(),
            Items: [new CreateOrderItemRequest(Guid.NewGuid(), "Cafeteira", 1, ValorAprovado)]);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await fixture.OrdersClient.SendAsync(message);
        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();

        body.Should().NotBeNull(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, body!);
    }

    private Task<GetOrderResponse?> ConsultarAsync(Guid orderId) =>
        fixture.OrdersClient.GetFromJsonAsync<GetOrderResponse>($"/orders/{orderId}");

    private async Task<GetOrderResponse> AguardarStatusAsync(
        Guid orderId, string statusEsperado, TimeSpan? timeout = null) =>
        await Eventually.UntilAsync(
            async () =>
            {
                var order = await ConsultarAsync(orderId);

                return order?.Status == statusEsperado ? order : null;
            },
            $"pedido {orderId} chegar a {statusEsperado}",
            timeout ?? TempoDeConvergencia);

    private async Task<IReadOnlyList<NotificationResponse>> AguardarNotificacoesAsync(
        Guid orderId, TimeSpan? timeout = null) =>
        await Eventually.UntilAsync(
            async () =>
            {
                var notificacoes = await fixture.NotificationsClient
                    .GetFromJsonAsync<List<NotificationResponse>>($"/notifications/order/{orderId}");

                return notificacoes is { Count: > 0 } ? notificacoes : null;
            },
            $"notificação do pedido {orderId} ser registrada",
            timeout ?? TempoDeConvergencia);
}
