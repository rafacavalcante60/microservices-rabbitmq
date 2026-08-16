namespace OrderService.Domain.Tests;

public class OrderStatusTransitionTests
{
    private static Order PendingOrder() =>
        new(Guid.NewGuid(), "chave-1", new[] { new OrderItem(Guid.NewGuid(), "Produto", 1, 10.00m) });

    private static Order OrderIn(OrderStatus status)
    {
        var order = PendingOrder();

        switch (status)
        {
            case OrderStatus.Paid:
                order.MarkAsPaid();
                break;
            case OrderStatus.PaymentDeclined:
                order.MarkAsDeclined("recusado");
                break;
            case OrderStatus.PaymentFailed:
                order.MarkAsFailed("gateway fora");
                break;
        }

        return order;
    }

    [Fact] // RN-7
    public void Pendente_vira_pago()
    {
        var order = PendingOrder();

        order.MarkAsPaid();

        order.Status.Should().Be(OrderStatus.Paid);
        order.StatusReason.Should().BeNull();
    }

    [Fact] // RN-7
    public void Pendente_vira_recusado_com_motivo()
    {
        var order = PendingOrder();

        order.MarkAsDeclined("limite excedido");

        order.Status.Should().Be(OrderStatus.PaymentDeclined);
        order.StatusReason.Should().Be("limite excedido");
    }

    [Fact] // RN-7, RN-11
    public void Pendente_vira_falhou_com_motivo()
    {
        var order = PendingOrder();

        order.MarkAsFailed("gateway indisponível");

        order.Status.Should().Be(OrderStatus.PaymentFailed);
        order.StatusReason.Should().Be("gateway indisponível");
    }

    [Fact]
    public void Transicao_atualiza_o_instante_de_alteracao()
    {
        var order = PendingOrder();
        var before = order.UpdatedAt;

        order.MarkAsPaid();

        order.UpdatedAt.Should().BeOnOrAfter(before);
        order.CreatedAt.Should().Be(before);
    }

    [Theory] // CA-9: um pedido Pago que recebe MarkAsDeclined continua Pago
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.PaymentDeclined)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void Estado_final_ignora_qualquer_nova_transicao(OrderStatus final)
    {
        var order = OrderIn(final);
        var reason = order.StatusReason;

        var act = () =>
        {
            order.MarkAsPaid();
            order.MarkAsDeclined("outro motivo");
            order.MarkAsFailed("outro motivo");
        };

        act.Should().NotThrow(); // desfecho duplicado é rotina, não erro
        order.Status.Should().Be(final);
        order.StatusReason.Should().Be(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Desfecho_negativo_sem_motivo_e_recusado(string reason)
    {
        var declined = () => PendingOrder().MarkAsDeclined(reason);
        var failed = () => PendingOrder().MarkAsFailed(reason);

        declined.Should().Throw<ArgumentException>().WithMessage("*motivo*");
        failed.Should().Throw<ArgumentException>().WithMessage("*motivo*");
    }
}
