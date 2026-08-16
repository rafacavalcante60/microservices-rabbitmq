namespace OrderService.Application.CreateOrder;

// CA-3: não existe campo de total aqui. Um cliente que mandar "totalAmount" no
// JSON não recebe erro — o campo simplesmente não é lido, porque não há onde
// pousar. O total nasce da soma dos itens dentro do agregado e de nenhum outro
// lugar.
//
// `Items` é anulável de propósito: o desserializador entrega `null` quando a
// propriedade falta no corpo, e é a validação que precisa dar a mensagem sobre
// isso — não um NullReferenceException.
public record CreateOrderRequest(
    Guid CustomerId,
    IReadOnlyList<CreateOrderItemRequest>? Items);

public record CreateOrderItemRequest(
    Guid ProductId,
    string? ProductName,
    int Quantity,
    decimal UnitPrice);
