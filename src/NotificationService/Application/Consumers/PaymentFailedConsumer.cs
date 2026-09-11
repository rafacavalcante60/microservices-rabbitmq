using NotificationService.Infrastructure.Persistence;
using Shared.Contracts;

namespace NotificationService.Application.Consumers;

// RN-11. Distinto da recusa: aqui o processador não respondeu. A mensagem ao
// cliente é outra — não há nada que ele possa corrigir, é o sistema que falhou.
public class PaymentFailedConsumer(
    NotificationStore store,
    ILogger<PaymentFailedConsumer> logger)
    : NotificationConsumer<PaymentFailed>(store, logger)
{
    protected override Notification Build(PaymentFailed message) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = message.OrderId,
        CustomerId = message.CustomerId,
        Type = NotificationType.PaymentFailed,
        Message = $"Não foi possível processar o pagamento de {Money(message.Amount)} após {message.Attempts} tentativas.",
        Reason = message.Reason,
        CreatedAt = DateTime.UtcNow,
        CorrelationId = message.CorrelationId
    };
}
