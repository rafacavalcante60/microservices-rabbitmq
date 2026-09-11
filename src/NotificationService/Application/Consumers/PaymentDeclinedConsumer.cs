using NotificationService.Infrastructure.Persistence;
using Shared.Contracts;

namespace NotificationService.Application.Consumers;

// CA-7: a recusa é comunicada **com o motivo**. É por isso que o Reason viaja
// no contrato desde o PaymentService: notificação que só diz "recusado" obriga
// o cliente a abrir chamado para descobrir o porquê.
public class PaymentDeclinedConsumer(
    NotificationStore store,
    ILogger<PaymentDeclinedConsumer> logger)
    : NotificationConsumer<PaymentDeclined>(store, logger)
{
    protected override Notification Build(PaymentDeclined message) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = message.OrderId,
        CustomerId = message.CustomerId,
        Type = NotificationType.PaymentDeclined,
        Message = $"Pagamento de {Money(message.Amount)} recusado.",
        Reason = message.Reason,
        CreatedAt = DateTime.UtcNow,
        CorrelationId = message.CorrelationId
    };
}
