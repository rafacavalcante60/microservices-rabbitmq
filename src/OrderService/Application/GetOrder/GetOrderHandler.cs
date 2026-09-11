using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.GetOrder;

public class GetOrderHandler(OrdersDbContext dbContext)
{
    // Devolve `null` quando não existe, em vez de lançar. Pedido inexistente é
    // uma resposta possível da consulta (FA-5), não uma falha do sistema — e
    // exceção para controlar fluxo esperado esconde a intenção e custa caro.
    // Quem traduz o `null` em 404 é o endpoint, que é a camada que conhece HTTP.
    public async Task<GetOrderResponse?> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // AsNoTracking: leitura pura, ninguém vai alterar este pedido nesta
        // requisição. Sem o rastreamento o EF não monta o snapshot de mudanças
        // de cada entidade, que é trabalho jogado fora numa consulta.
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        return order is null ? null : ToResponse(order);
    }

    private static GetOrderResponse ToResponse(Order order) =>
        new(order.Id,
            order.CustomerId,
            order.Status.ToString(),
            order.StatusReason,
            order.TotalAmount,
            order.Currency,
            order.Items.Select(item => new GetOrderItemResponse(
                item.ProductId,
                item.ProductName,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal)).ToList(),
            order.CreatedAt,
            order.UpdatedAt);
}
