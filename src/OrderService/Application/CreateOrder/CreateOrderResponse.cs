namespace OrderService.Application.CreateOrder;

// RN-14: a resposta se fecha em Pending. O desfecho da cobrança ainda não
// existe neste instante e esperar por ele seria transformar um fluxo assíncrono
// em síncrono — exatamente o que o projeto existe para não fazer.
public record CreateOrderResponse(Guid OrderId, string Status, decimal TotalAmount);
