using MassTransit;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using Shared.Contracts;

namespace OrderService.Application.CreateOrder;

public class CreateOrderHandler(OrdersDbContext dbContext, IPublishEndpoint publishEndpoint)
{
    public async Task<CreateOrderResponse> HandleAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var items = request.Items!.Select(item => new OrderItem(
            item.ProductId,
            item.ProductName!,
            item.Quantity,
            item.UnitPrice));

        var order = new Order(request.CustomerId, idempotencyKey, items);

        dbContext.Orders.Add(order);

        // Princípio VII: o CorrelationId deveria nascer na borda, no gateway
        // (T-021). Enquanto o gateway não existe, ele nasce aqui — o importante
        // é que o evento nunca viaje sem um.
        var correlationId = Guid.NewGuid();

        // Este `Publish` não fala com o RabbitMQ. Por causa do `UseBusOutbox()`
        // configurado em T-007, o IPublishEndpoint deste escopo grava o evento
        // como uma linha na tabela outbox_message do próprio OrdersDbContext.
        // Nada sai daqui ainda.
        await publishEndpoint.Publish(
            new OrderCreated(
                order.Id,
                order.CustomerId,
                order.TotalAmount,
                order.Currency,
                DateTime.UtcNow,
                correlationId),
            cancellationToken);

        // É este SaveChanges que decide o destino dos dois: o pedido, os itens e
        // a linha do outbox entram na mesma transação. Ou tudo é gravado, ou
        // nada é — não existe o estado "pedido existe mas ninguém vai cobrá-lo".
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponse(order.Id, order.Status.ToString(), order.TotalAmount);
    }
}
