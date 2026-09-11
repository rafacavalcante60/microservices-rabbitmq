using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using Shared.Contracts;

namespace OrderService.Application.Consumers;

// CA-7: recusa leva a PaymentDeclined carregando o motivo dado pelo gateway,
// que é o que o cliente vê em GET /orders/{id}.
public class PaymentDeclinedConsumer(
    OrdersDbContext dbContext,
    ILogger<PaymentDeclinedConsumer> logger)
    : PaymentOutcomeConsumer<PaymentDeclined>(dbContext, logger)
{
    protected override (Guid OrderId, Guid CorrelationId) Identify(PaymentDeclined message) =>
        (message.OrderId, message.CorrelationId);

    protected override void Apply(Order order, PaymentDeclined message) =>
        order.MarkAsDeclined(message.Reason);
}
