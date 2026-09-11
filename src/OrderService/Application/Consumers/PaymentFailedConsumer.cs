using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using Shared.Contracts;

namespace OrderService.Application.Consumers;

// RN-11. Falhou é estado final distinto de recusado: o gateway não respondeu em
// nenhuma tentativa. O pedido para em PaymentFailed em vez de ficar Pending
// para sempre — um estado terminal explícito é o que permite a alguém decidir
// depois o que fazer com ele.
public class PaymentFailedConsumer(
    OrdersDbContext dbContext,
    ILogger<PaymentFailedConsumer> logger)
    : PaymentOutcomeConsumer<PaymentFailed>(dbContext, logger)
{
    protected override (Guid OrderId, Guid CorrelationId) Identify(PaymentFailed message) =>
        (message.OrderId, message.CorrelationId);

    protected override void Apply(Order order, PaymentFailed message) =>
        order.MarkAsFailed(message.Reason);
}
