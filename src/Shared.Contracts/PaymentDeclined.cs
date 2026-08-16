namespace Shared.Contracts;

/// Publicado quando o gateway responde recusando a cobrança (RN-10).
/// O Reason é o motivo mostrado ao cliente na notificação (CA-7).
public record PaymentDeclined(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Reason,
    DateTime OccurredAt,
    Guid CorrelationId);
