namespace OrderService.Application.GetOrder;

// Diferente do CreateOrderResponse, que se fecha em Pending de propósito
// (RN-14), esta resposta mostra o estado atual — é para ela que o operador
// olha quando o cliente liga perguntando do pedido (CA-15).
//
// `StatusReason` é anulável porque só existe em desfecho negativo: um pedido
// Pago não tem motivo, e um campo vazio diz isso melhor que uma string em
// branco.
public record GetOrderResponse(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    string? StatusReason,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<GetOrderItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// `LineTotal` viaja calculado, mesmo não existindo como coluna: quem consulta
// para dar suporte quer conferir a conta sem multiplicar à mão. O número nasce
// do domínio, então não há risco de divergir do total.
public record GetOrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
