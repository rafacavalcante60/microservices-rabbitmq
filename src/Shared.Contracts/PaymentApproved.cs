namespace Shared.Contracts;

/// Publicado pelo PaymentService quando o gateway aprova a cobrança.
/// Consumido pelo OrderService e pelo NotificationService.
public record PaymentApproved(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    DateTime OccurredAt,
    Guid CorrelationId);
