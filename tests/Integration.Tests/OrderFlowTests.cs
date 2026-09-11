using System.Net;
using System.Net.Http.Json;
using Integration.Tests.Fixtures;
using NotificationService.Application.GetNotifications;
using NotificationService.Infrastructure.Persistence;
using OrderService.Application.CreateOrder;
using OrderService.Application.GetOrder;
using PaymentService.Domain;

namespace Integration.Tests;

// Os dois caminhos completos da spec, atravessando os três serviços por cima do
// RabbitMQ de verdade: POST no OrderService, decisão no PaymentService,
// notificação no NotificationService — nenhum deles chamando o outro.
//
// É o teste que justifica a arquitetura inteira. Um mock de broker aqui
// verificaria que o código publica; só o broker real verifica que a mensagem
// chega, desserializa no tipo certo e acorda os dois consumidores que a assinam.
[Collection(IntegrationCollection.Name)]
public class OrderFlowTests(IntegrationFixture fixture)
{
    // Abaixo do limite de RN-10 por uma margem larga: o teste é do fluxo, não da
    // fronteira. O centavo em cima e embaixo do limite é exercício de unidade,
    // onde custa milissegundos em vez de contêiner.
    private const decimal ValorAprovado = 500.00m;

    [Fact]
    public async Task Fluxo_aprovado_paga_o_pedido_e_gera_uma_notificacao_de_confirmacao()
    {
        var (orderId, created) = await CriarPedidoAsync(ValorAprovado);

        // CA-1: a criação responde Pending, e não o desfecho. O pagamento ainda
        // nem começou neste instante — prometer "Paid" aqui seria mentir sobre
        // trabalho que está na fila, e é justamente o que consistência eventual
        // obriga a encarar de frente.
        created.Status.Should().Be("Pending");
        created.TotalAmount.Should().Be(ValorAprovado);

        // CA-15: a consulta reflete o estado final, depois que a cadeia
        // assíncrona converge.
        var order = await AguardarStatusAsync(orderId, "Paid");
        order.StatusReason.Should().BeNull("pedido pago não tem motivo a explicar");

        // CA-6: exatamente uma. Os dois serviços consomem o mesmo PaymentApproved
        // em filas próprias (D-15), e o índice único em orderId impede que uma
        // reentrega vire documento extra.
        var notifications = await AguardarNotificacoesAsync(orderId);
        notifications.Should().ContainSingle();

        var notification = notifications.Single();
        notification.Type.Should().Be(NotificationType.OrderPaid);
        notification.Reason.Should().BeNull();
        notification.Message.Should().Contain("500,00");
        notification.CustomerId.Should().Be(order.CustomerId);
    }

    [Fact]
    public async Task Fluxo_recusado_marca_o_pedido_e_notifica_com_o_motivo()
    {
        // CA-7 e CA-8: um centavo acima do limite. O valor não é arbitrário — é
        // a menor quantia que separa aprovar de recusar, e é o número que denuncia
        // um `<` escrito onde devia ser `<=`.
        var valorRecusado = PaymentApprovalRule.ApprovalLimit + 0.01m;

        var (orderId, _) = await CriarPedidoAsync(valorRecusado);

        var order = await AguardarStatusAsync(orderId, "PaymentDeclined");
        order.StatusReason.Should().NotBeNullOrWhiteSpace(
            "recusa sem motivo obriga o cliente a abrir chamado para descobrir o porquê");

        var notifications = await AguardarNotificacoesAsync(orderId);
        notifications.Should().ContainSingle();

        var notification = notifications.Single();
        notification.Type.Should().Be(NotificationType.PaymentDeclined);
        notification.Reason.Should().Be(PaymentApprovalRule.DeclineReasonFor(valorRecusado));
        notification.Reason.Should().Contain("10.000,01");
    }

    private async Task<(Guid OrderId, CreateOrderResponse Response)> CriarPedidoAsync(decimal valor)
    {
        var request = new CreateOrderRequest(
            CustomerId: Guid.NewGuid(),
            Items: [new CreateOrderItemRequest(Guid.NewGuid(), "Cafeteira", 1, valor)]);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(request)
        };

        // RN-8: obrigatória em toda criação. Uma por pedido aqui, porque o que
        // esta classe testa é o fluxo — o comportamento da chave repetida é da
        // T-025.
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await fixture.OrdersClient.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created, because: body);

        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        created.Should().NotBeNull();

        return (created!.OrderId, created);
    }

    // O timeout é generoso de propósito: entre o 201 e o estado final passam o
    // outbox do OrderService, duas filas, a decisão do pagamento e o outbox do
    // PaymentService. O polling devolve assim que converge — quem paga os 30s
    // inteiros é só a execução que realmente quebrou, e aí a espera longa é o
    // que diferencia "o sistema não convergiu" de "a máquina estava lenta".
    private static readonly TimeSpan TempoDeConvergencia = TimeSpan.FromSeconds(30);

    private async Task<GetOrderResponse> AguardarStatusAsync(Guid orderId, string statusEsperado) =>
        await Eventually.UntilAsync(
            async () =>
            {
                var order = await fixture.OrdersClient.GetFromJsonAsync<GetOrderResponse>($"/orders/{orderId}");

                return order?.Status == statusEsperado ? order : null;
            },
            $"pedido {orderId} chegar a {statusEsperado}",
            TempoDeConvergencia);

    private async Task<IReadOnlyList<NotificationResponse>> AguardarNotificacoesAsync(Guid orderId) =>
        await Eventually.UntilAsync(
            async () =>
            {
                var notifications = await fixture.NotificationsClient
                    .GetFromJsonAsync<List<NotificationResponse>>($"/notifications/order/{orderId}");

                // Lista vazia é a resposta normal enquanto a notificação está a
                // caminho (ver NotificationsEndpoints), então "ainda não" aqui é
                // vazio, não nulo.
                return notifications is { Count: > 0 } ? notifications : null;
            },
            $"notificação do pedido {orderId} ser registrada",
            TempoDeConvergencia);
}
