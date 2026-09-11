using System.Net;
using System.Net.Http.Json;
using Integration.Tests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.GetNotifications;
using OrderService.Application.CreateOrder;
using OrderService.Application.GetOrder;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Persistence;
using Shared.Contracts;

namespace Integration.Tests;

// As duas idempotências do sistema, que resolvem problemas diferentes e por
// mecanismos diferentes — e é essa distinção que o conjunto abaixo fixa:
//
//   Na **borda**, o cliente pode repetir o POST porque a resposta se perdeu na
//   rede. Quem decide é ele, com a chave de idempotência, e quem garante é o
//   índice único `(customer_id, idempotency_key)` (D-5).
//
//   No **consumo**, o broker pode reentregar a mesma mensagem porque o `ack` se
//   perdeu. Ninguém decide — é a garantia at-least-once do RabbitMQ, reforçada
//   pelo outbox. Quem garante é o MessageId no Redis (D-4), com a constraint
//   única em `payments.order_id` como rede embaixo.
//
// Confundir as duas é erro comum: a chave da borda não protegeria contra
// reentrega, e o MessageId não protegeria contra o cliente que aperta o botão
// duas vezes.
[Collection(IntegrationCollection.Name)]
public class IdempotencyTests(IntegrationFixture fixture)
{
    private const decimal ValorAprovado = 500.00m;

    [Fact]
    public async Task Mesma_chave_duas_vezes_gera_um_unico_pedido()
    {
        var customerId = Guid.NewGuid();
        var chave = Guid.NewGuid().ToString();
        var request = PedidoDe(customerId);

        var primeira = await CriarAsync(request, chave);
        var segunda = await CriarAsync(request, chave);

        // CA-11. O 201 diz "criei"; o 200 diz "isto já existe, e é seu". A
        // segunda requisição não é erro — é a mesma intenção chegando de novo,
        // e responder 409 obrigaria o cliente a tratar como falha aquilo que
        // deu certo na primeira tentativa.
        primeira.Status.Should().Be(HttpStatusCode.Created);
        segunda.Status.Should().Be(HttpStatusCode.OK);

        // O mesmo identificador nas duas respostas é o que torna a repetição
        // útil: o cliente que perdeu o primeiro 201 recupera o id do pedido em
        // vez de ficar sem saber o que aconteceu com ele.
        segunda.Body.OrderId.Should().Be(primeira.Body.OrderId);
        segunda.Body.TotalAmount.Should().Be(primeira.Body.TotalAmount);

        // E um único pedido no banco, que é a afirmação que realmente importa:
        // duas respostas coerentes com duas linhas gravadas seria o pior dos
        // mundos — o cliente cobrado duas vezes sem nunca perceber.
        var pedidos = await ContarPedidosAsync(customerId);
        pedidos.Should().Be(1);
    }

    [Fact]
    public async Task Chaves_diferentes_geram_pedidos_distintos()
    {
        var customerId = Guid.NewGuid();
        var request = PedidoDe(customerId);

        // CA-12. Conteúdo idêntico, chaves diferentes. É o caso que prova que a
        // idempotência não é deduplicação por conteúdo: comprar duas vezes a
        // mesma cafeteira é intenção legítima, e só o cliente sabe distinguir
        // isso de uma retentativa. Qualquer heurística sobre o corpo da
        // requisição erraria justamente aqui.
        var primeira = await CriarAsync(request, Guid.NewGuid().ToString());
        var segunda = await CriarAsync(request, Guid.NewGuid().ToString());

        primeira.Status.Should().Be(HttpStatusCode.Created);
        segunda.Status.Should().Be(HttpStatusCode.Created);
        segunda.Body.OrderId.Should().NotBe(primeira.Body.OrderId);

        var pedidos = await ContarPedidosAsync(customerId);
        pedidos.Should().Be(2);
    }

    [Fact]
    public async Task OrderCreated_reentregue_nao_cobra_nem_notifica_de_novo()
    {
        // RN-9 / RN-12 / CA-10. Aqui o pedido é gravado direto no banco, sem
        // passar pelo POST, e o OrderCreated é publicado à mão. Não é atalho: é
        // a única forma de controlar o MessageId, que é o que está sob teste. O
        // caminho normal publica pelo outbox, que gera um MessageId novo a cada
        // linha — e duas mensagens com identificadores diferentes são, para o
        // consumidor, dois eventos distintos.
        //
        // O pedido precisa existir de verdade porque o desfecho da cobrança
        // volta para o OrderService: um OrderCreated inventado levaria o
        // consumidor de desfecho a falhar e encher a DLQ de ruído que não tem
        // nada a ver com o que se quer verificar.
        var order = await SemearPedidoAsync(ValorAprovado);
        var evento = new OrderCreated(
            order.Id, order.CustomerId, order.TotalAmount, order.Currency,
            DateTime.UtcNow, CorrelationId: Guid.NewGuid());

        var messageId = NewId.NextGuid();

        await PublicarAsync(evento, messageId);

        // A duplicata é publicada **depois** da convergência, e não em rajada:
        // assim ela chega a um consumidor que já terminou de processar a
        // primeira, que é exatamente a reentrega tardia do broker. Publicar as
        // duas juntas testaria concorrência, que é outro problema.
        await AguardarConvergenciaAsync(order.Id);
        await PublicarAsync(evento, messageId);

        // Provar que *nada* aconteceu não tem condição para esperar, então a
        // verificação é o contrário do polling: repetir a checagem durante uma
        // janela e exigir que o número nunca saia de um. Se o filtro do Redis
        // deixasse a duplicata passar, o segundo pagamento apareceria dentro
        // dela — o caminho inteiro leva menos de um segundo no teste acima.
        await DuranteAsync(
            TimeSpan.FromSeconds(5),
            async () =>
            {
                var pagamentos = await ContarPagamentosAsync(order.Id);
                pagamentos.Should().Be(1, "a cobrança é registrada uma única vez (RN-9)");

                var notificacoes = await BuscarNotificacoesAsync(order.Id);
                notificacoes.Should().ContainSingle("o cliente recebe uma notificação por pedido (RN-12)");
            });
    }

    private static CreateOrderRequest PedidoDe(Guid customerId) =>
        new(customerId, [new CreateOrderItemRequest(Guid.NewGuid(), "Cafeteira", 1, ValorAprovado)]);

    private async Task<(HttpStatusCode Status, CreateOrderResponse Body)> CriarAsync(
        CreateOrderRequest request, string idempotencyKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await fixture.OrdersClient.SendAsync(message);
        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();

        body.Should().NotBeNull(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, body!);
    }

    private async Task<Order> SemearPedidoAsync(decimal valor)
    {
        var order = new Order(
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            [new OrderItem(Guid.NewGuid(), "Cafeteira", 1, valor)]);

        using var scope = fixture.Orders.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        return order;
    }

    // Pelo IBus da raiz, e não pelo IPublishEndpoint de um escopo: o escopo tem
    // o outbox ligado e gravaria o evento na tabela em vez de mandá-lo ao
    // broker — com um MessageId escolhido pela biblioteca, não por este teste.
    private async Task PublicarAsync(OrderCreated evento, Guid messageId)
    {
        var bus = fixture.Orders.Services.GetRequiredService<IBus>();

        await bus.Publish(evento, context => context.MessageId = messageId);
    }

    private async Task AguardarConvergenciaAsync(Guid orderId)
    {
        await Eventually.UntilAsync(
            async () =>
            {
                var order = await fixture.OrdersClient.GetFromJsonAsync<GetOrderResponse>($"/orders/{orderId}");

                return order?.Status == "Paid" ? order : null;
            },
            $"pedido {orderId} chegar a Paid",
            TimeSpan.FromSeconds(30));

        await Eventually.UntilAsync(
            async () => (await BuscarNotificacoesAsync(orderId)).Count > 0,
            $"notificação do pedido {orderId} ser registrada",
            TimeSpan.FromSeconds(30));
    }

    private async Task<IReadOnlyList<NotificationResponse>> BuscarNotificacoesAsync(Guid orderId) =>
        await fixture.NotificationsClient
            .GetFromJsonAsync<List<NotificationResponse>>($"/notifications/order/{orderId}")
            ?? [];

    private async Task<int> ContarPedidosAsync(Guid customerId)
    {
        using var scope = fixture.Orders.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        return await dbContext.Orders.CountAsync(order => order.CustomerId == customerId);
    }

    private async Task<int> ContarPagamentosAsync(Guid orderId)
    {
        using var scope = fixture.Payments.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        return await dbContext.Payments.CountAsync(payment => payment.OrderId == orderId);
    }

    // O oposto de Eventually: em vez de esperar uma condição passar a valer,
    // exige que ela continue valendo durante a janela inteira. Existe porque
    // "nada mais aconteceu" é uma afirmação sobre o futuro, e nenhuma espera
    // curta a prova — o que se pode fazer é dar ao efeito indesejado tempo de
    // sobra para aparecer e verificar que ele não apareceu.
    private static async Task DuranteAsync(TimeSpan janela, Func<Task> verificacao)
    {
        var limite = DateTime.UtcNow + janela;

        do
        {
            await verificacao();
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        while (DateTime.UtcNow < limite);
    }
}
