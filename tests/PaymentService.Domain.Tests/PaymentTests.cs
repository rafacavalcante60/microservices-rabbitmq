namespace PaymentService.Domain.Tests;

public class PaymentTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact] // CA-6 — caminho feliz
    public void Pagamento_de_500_reais_nasce_aprovado_e_sem_motivo()
    {
        var payment = Payment.For(OrderId, CustomerId, 500.00m, attempts: 1);

        payment.Status.Should().Be(PaymentStatus.Approved);
        payment.Reason.Should().BeNull();
        payment.Amount.Should().Be(500.00m);
        payment.OrderId.Should().Be(OrderId);
    }

    [Fact] // CA-8 — o limite exato aprova
    public void Pagamento_no_limite_exato_nasce_aprovado()
    {
        var payment = Payment.For(OrderId, CustomerId, 10_000.00m, attempts: 1);

        payment.Status.Should().Be(PaymentStatus.Approved);
    }

    [Fact] // CA-7 — o centavo acima recusa, com motivo
    public void Pagamento_um_centavo_acima_do_limite_nasce_recusado_com_motivo()
    {
        var payment = Payment.For(OrderId, CustomerId, 10_000.01m, attempts: 1);

        payment.Status.Should().Be(PaymentStatus.Declined);
        payment.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // FA-3 — falhou é distinto de recusado
    public void Falha_tecnica_e_registrada_como_Failed_e_nao_como_Declined()
    {
        var payment = Payment.Failed(OrderId, CustomerId, 500.00m, "gateway não respondeu", attempts: 5);

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.Attempts.Should().Be(5);
    }

    [Fact] // RN-11 — o contador de tentativas é o que distingue "de primeira" de "na quinta"
    public void Numero_de_tentativas_e_preservado()
    {
        Payment.For(OrderId, CustomerId, 500.00m, attempts: 3).Attempts.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Cobranca_sem_tentativa_nao_existe(int attempts)
    {
        var criar = () => Payment.For(OrderId, CustomerId, 500.00m, attempts);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Valor_precisa_ser_maior_que_zero(decimal amount)
    {
        var criar = () => Payment.For(OrderId, CustomerId, amount, attempts: 1);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Pagamento_precisa_de_pedido()
    {
        var criar = () => Payment.For(Guid.Empty, CustomerId, 500.00m, attempts: 1);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Pagamento_precisa_de_cliente()
    {
        var criar = () => Payment.For(OrderId, Guid.Empty, 500.00m, attempts: 1);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact] // desfecho negativo sem motivo esconde a causa de quem dá suporte
    public void Desfecho_negativo_exige_motivo()
    {
        var recusar = () => Payment.Declined(OrderId, CustomerId, 500.00m, "  ", attempts: 1);
        var falhar = () => Payment.Failed(OrderId, CustomerId, 500.00m, "", attempts: 1);

        recusar.Should().Throw<ArgumentException>();
        falhar.Should().Throw<ArgumentException>();
    }
}
