using FluentValidation;
using OrderService.Domain;

namespace OrderService.Application.CreateOrder;

// D-11: as mesmas regras aparecem aqui e no construtor do agregado, e essa
// duplicação é deliberada. O agregado lança na primeira violação que encontra —
// é a garantia de que não existe `Order` inválido, venha ele de onde vier. A
// borda precisa de outra coisa: juntar *todas* as violações e devolvê-las de
// uma vez, para o cliente corrigir tudo numa tentativa só (CA-4, CA-5). Uma
// exceção por vez daria uma conversa de seis idas e voltas.
//
// A alternativa descartada foi validar só na borda: aí o domínio passaria a
// depender de quem o chama, e qualquer caminho novo — um consumidor, um script
// de carga — poderia gravar lixo no banco.
public class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(request => request.CustomerId)
            .NotEmpty()
            .WithMessage("O pedido precisa identificar o cliente."); // RN-15

        // RN-1. `NotEmpty` numa coleção cobre os dois casos de uma vez: lista
        // vazia e campo ausente no JSON, que chega aqui como nulo.
        RuleFor(request => request.Items)
            .NotEmpty()
            .WithMessage("O pedido precisa de ao menos um item.");

        RuleForEach(request => request.Items)
            .SetValidator(new CreateOrderItemValidator())
            .When(request => request.Items is not null);
    }
}

public class CreateOrderItemValidator : AbstractValidator<CreateOrderItemRequest>
{
    public CreateOrderItemValidator()
    {
        RuleFor(item => item.ProductId)
            .NotEmpty()
            .WithMessage("O item precisa identificar o produto.");

        RuleFor(item => item.ProductName)
            .NotEmpty()
            .WithMessage("O item precisa do nome do produto.");

        // RN-2
        RuleFor(item => item.Quantity)
            .InclusiveBetween(OrderItem.MinQuantity, OrderItem.MaxQuantity)
            .WithMessage($"A quantidade precisa estar entre {OrderItem.MinQuantity} e {OrderItem.MaxQuantity}.");

        // RN-3
        RuleFor(item => item.UnitPrice)
            .GreaterThan(0m)
            .WithMessage("O preço unitário precisa ser maior que zero.");
    }
}
