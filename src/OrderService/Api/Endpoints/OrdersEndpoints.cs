using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.CreateOrder;

namespace OrderService.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", CreateOrderAsync)
            .WithName("CreateOrder");

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        IValidator<CreateOrderRequest> validator,
        CreateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);

        // FA-1: recusado na borda, nada chega ao banco. O 400 sai como
        // ProblemDetails com o dicionário de violações — formato padrão do
        // ASP.NET, que qualquer cliente já sabe ler.
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        // T-009 torna o header obrigatório e trata a repetição da mesma chave.
        // Até lá, a chave é gerada quando o cliente não manda uma: a coluna é
        // obrigatória no banco desde T-006 e o agregado a exige desde T-004.
        var response = await handler.HandleAsync(
            request,
            idempotencyKey ?? Guid.NewGuid().ToString(),
            cancellationToken);

        // 201 com Location, e não 200: a requisição criou um recurso novo e
        // agora ele tem endereço próprio — o GET /orders/{id} da T-010.
        return Results.Created($"/orders/{response.OrderId}", response);
    }
}
