namespace OrderService.Domain.Tests;

public class OrderTests
{
    private static OrderItem Item(int quantity = 1, decimal unitPrice = 10m) =>
        new(Guid.NewGuid(), "Produto", quantity, unitPrice);

    private static Order NewOrder(params OrderItem[] items) =>
        new(Guid.NewGuid(), "chave-1", items);

    [Fact] // CA-2, RN-4
    public void Total_e_a_soma_de_quantidade_vezes_preco()
    {
        var order = NewOrder(Item(2, 10.00m), Item(3, 5.00m));

        order.TotalAmount.Should().Be(35.00m);
    }

    [Fact] // CA-3, RN-4
    public void Total_declarado_pelo_cliente_nao_tem_como_ser_informado()
    {
        // O construtor não expõe parâmetro de total; o valor de 1,00 que o
        // cliente declararia não tem por onde entrar no agregado.
        var order = NewOrder(Item(2, 10.00m), Item(3, 5.00m));

        order.TotalAmount.Should().Be(35.00m);
        typeof(Order).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Should().NotContain(p => p.Name!.Contains("total", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // CA-4, RN-1
    public void Pedido_sem_itens_e_recusado()
    {
        var act = () => NewOrder();

        act.Should().Throw<ArgumentException>().WithMessage("*ao menos um item*");
    }

    [Theory] // CA-5, RN-2
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Quantidade_fora_de_1_a_100_e_recusada(int quantity)
    {
        var act = () => Item(quantity);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*entre 1 e 100*");
    }

    [Theory] // CA-5, RN-2
    [InlineData(1)]
    [InlineData(100)]
    public void Quantidade_no_limite_e_aceita(int quantity)
    {
        var act = () => Item(quantity);

        act.Should().NotThrow();
    }

    [Theory] // CA-5, RN-3
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Preco_unitario_menor_ou_igual_a_zero_e_recusado(decimal unitPrice)
    {
        var act = () => Item(1, unitPrice);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maior que zero*");
    }

    [Fact] // CA-1, RN-6
    public void Pedido_nasce_pendente_em_reais()
    {
        var order = NewOrder(Item());

        order.Status.Should().Be(OrderStatus.Pending);
        order.Currency.Should().Be("BRL");
        order.Id.Should().NotBeEmpty();
    }

    [Fact] // RN-8: a chave é o que identifica a repetição do cliente
    public void Pedido_guarda_cliente_e_chave_de_idempotencia()
    {
        var customerId = Guid.NewGuid();

        var order = new Order(customerId, "chave-abc", new[] { Item() });

        order.CustomerId.Should().Be(customerId);
        order.IdempotencyKey.Should().Be("chave-abc");
    }

    [Fact]
    public void Pedido_sem_cliente_e_recusado()
    {
        var act = () => new Order(Guid.Empty, "chave-1", new[] { Item() });

        act.Should().Throw<ArgumentException>().WithMessage("*cliente*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Pedido_sem_chave_de_idempotencia_e_recusado(string key)
    {
        var act = () => new Order(Guid.NewGuid(), key, new[] { Item() });

        act.Should().Throw<ArgumentException>().WithMessage("*idempotência*");
    }

    [Fact]
    public void Itens_do_pedido_nao_podem_ser_alterados_por_fora()
    {
        var items = new List<OrderItem> { Item(2, 10.00m) };
        var order = new Order(Guid.NewGuid(), "chave-1", items);

        items.Add(Item(5, 100.00m));

        order.Items.Should().HaveCount(1);
        order.TotalAmount.Should().Be(20.00m);
    }
}
