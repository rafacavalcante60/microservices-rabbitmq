using NotificationService.Infrastructure.Persistence;
using Shared.Contracts;

namespace NotificationService.Application.Consumers;

// CA-6: exatamente uma notificação de confirmação por pedido aprovado.
public class PaymentApprovedConsumer(
    NotificationStore store,
    ILogger<PaymentApprovedConsumer> logger)
    : NotificationConsumer<PaymentApproved>(store, logger)
{
    protected override Notification Build(PaymentApproved message) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = message.OrderId,
        CustomerId = message.CustomerId,
        Type = NotificationType.OrderPaid,
        Message = $"Pagamento de {Money(message.Amount)} confirmado. Seu pedido está sendo preparado.",
        CreatedAt = DateTime.UtcNow,
        CorrelationId = message.CorrelationId
    };
}
