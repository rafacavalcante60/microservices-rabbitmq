namespace Shared.Contracts;

/// Publicado quando o gateway não respondeu em nenhuma das tentativas (RN-11).
// Falhou é distinto de recusado: recusado é decisão do processador, falhou é
// ausência de resposta. Attempts registra quantas tentativas foram gastas.
public record PaymentFailed(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Reason,
    int Attempts,
    DateTime OccurredAt,
    Guid CorrelationId);
