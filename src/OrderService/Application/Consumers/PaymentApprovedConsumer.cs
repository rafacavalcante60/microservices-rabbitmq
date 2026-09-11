using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using Shared.Contracts;

namespace OrderService.Application.Consumers;

// CA-6: pagamento aprovado leva o pedido a Paid. Sem motivo — desfecho positivo
// não precisa se justificar.
public class PaymentApprovedConsumer(
    OrdersDbContext dbContext,
    ILogger<PaymentApprovedConsumer> logger)
    : PaymentOutcomeConsumer<PaymentApproved>(dbContext, logger)
{
    protected override (Guid OrderId, Guid CorrelationId) Identify(PaymentApproved message) =>
        (message.OrderId, message.CorrelationId);

    protected override void Apply(Order order, PaymentApproved message) => order.MarkAsPaid();
}
