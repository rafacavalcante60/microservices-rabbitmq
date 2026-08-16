namespace Shared.Contracts;

/// Publicado pelo OrderService na mesma transação da criação do pedido.
/// Consumido pelo PaymentService.
// Os itens não viajam no evento: o PaymentService só precisa do total para
// cobrar. Contrato menor, acoplamento menor.
public record OrderCreated(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    string Currency,
    DateTime OccurredAt,
    Guid CorrelationId);
