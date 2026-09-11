using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.CreateOrder;
using OrderService.Application.GetOrder;

namespace OrderService.Api.Endpoints;

public static class OrdersEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", CreateOrderAsync)
            .WithName("CreateOrder");

        // A restrição `:guid` na rota não é enfeite: um id malformado deixa de
        // casar com a rota e vira 404 antes de chegar ao handler, em vez de
        // explodir na conversão. Lixo na URL e pedido inexistente são a mesma
        // resposta para quem consulta — nenhum dos dois existe.
        app.MapGet("/orders/{orderId:guid}", GetOrderAsync)
            .WithName("GetOrder");

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        IValidator<CreateOrderRequest> validator,
        CreateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        var violations = validation.ToDictionary();

        // RN-8: a chave é obrigatória e é o cliente quem a escolhe, porque só
        // ele sabe se está tentando de novo ou comprando outra vez. Nenhuma
        // heurística sobre o conteúdo acerta isso: dois pedidos idênticos em
        // sequência podem ser ambos intencionais.
        //
        // A violação entra no mesmo dicionário das outras em vez de sair num
        // retorno próprio — D-11 pede todas as violações de uma vez, e um
        // cliente que esqueceu o header e mandou um item inválido merece
        // descobrir as duas coisas na mesma resposta.
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            violations[IdempotencyKeyHeader] = [$"O header {IdempotencyKeyHeader} é obrigatório."];
        }

        // FA-1: recusado na borda, nada chega ao banco. O 400 sai como
        // ProblemDetails com o dicionário de violações — formato padrão do
        // ASP.NET, que qualquer cliente já sabe ler.
        if (violations.Count > 0)
        {
            return Results.ValidationProblem(violations);
        }

        // O `!` é necessário porque a garantia de não-nulo está espalhada em
        // dois passos: o `if` acima registrou a violação, e o `if` seguinte
        // saiu. A análise de fluxo do compilador não atravessa essa separação.
        var result = await handler.HandleAsync(request, idempotencyKey!, cancellationToken);

        // CA-11: 200 na repetição, e não 201, porque nada foi criado desta vez.
        // O cliente que não distingue os dois continua funcionando; o que
        // distingue, ganha a informação de que sua primeira tentativa venceu.
        // A alternativa era 201 sempre — mais simples, e mente sobre o que
        // aconteceu.
        if (result.AlreadyExisted)
        {
            return Results.Ok(result.Response);
        }

        // 201 com Location: a requisição criou um recurso novo e agora ele tem
        // endereço próprio — o GET /orders/{id} logo abaixo.
        return Results.Created($"/orders/{result.Response.OrderId}", result.Response);
    }

    private static async Task<IResult> GetOrderAsync(
        Guid orderId,
        GetOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(orderId, cancellationToken);

        // CA-16 / FA-5. O 404 sai como ProblemDetails, e não como corpo vazio,
        // para que a API tenha um formato de erro só: quem já sabe ler o 400 da
        // criação lê este sem código novo.
        if (order is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Pedido não encontrado.",
                detail: $"Não existe pedido com o identificador {orderId}.");
        }

        return Results.Ok(order);
    }
}
