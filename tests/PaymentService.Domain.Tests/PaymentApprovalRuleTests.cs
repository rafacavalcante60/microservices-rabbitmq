namespace PaymentService.Domain.Tests;

// RN-10. Toda a regra é uma comparação, e por isso o teste é quase todo sobre
// a fronteira: o erro provável aqui não é "esqueci de comparar", é "usei < onde
// devia ser <=". Um caso de cada lado não pega isso; três casos colados no
// limite pegam.
public class PaymentApprovalRuleTests
{
    [Theory] // CA-8 — o limite é inclusivo
    [InlineData(0.01)]
    [InlineData(500.00)]
    [InlineData(9_999.99)]
    [InlineData(10_000.00)] // exatamente o limite: aprova
    public void Aprova_valores_ate_o_limite(decimal amount)
    {
        PaymentApprovalRule.Approves(amount).Should().BeTrue();
    }

    [Theory] // CA-7 — acima do limite recusa
    [InlineData(10_000.01)] // o centavo acima: recusa
    [InlineData(10_001.00)]
    [InlineData(1_000_000.00)]
    public void Recusa_valores_acima_do_limite(decimal amount)
    {
        PaymentApprovalRule.Approves(amount).Should().BeFalse();
    }

    [Fact] // CA-8
    public void Limite_exato_e_o_centavo_acima_caem_em_lados_opostos()
    {
        PaymentApprovalRule.Approves(PaymentApprovalRule.ApprovalLimit).Should().BeTrue();
        PaymentApprovalRule.Approves(PaymentApprovalRule.ApprovalLimit + 0.01m).Should().BeFalse();
    }

    [Fact] // RN-10 — determinismo
    public void Mesmo_valor_produz_sempre_o_mesmo_resultado()
    {
        var resultados = Enumerable.Range(0, 50)
            .Select(_ => PaymentApprovalRule.Approves(10_000.01m))
            .Distinct();

        resultados.Should().ContainSingle().Which.Should().BeFalse();
    }

    [Fact]
    public void Motivo_da_recusa_cita_o_valor_e_o_limite()
    {
        var reason = PaymentApprovalRule.DeclineReasonFor(10_000.01m);

        reason.Should().Contain("10.000,01").And.Contain("10.000,00");
    }
}
