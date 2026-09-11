using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentService.Infrastructure;

namespace PaymentService.Domain.Tests;

// D-8. O que se testa aqui não é a regra de aprovação — essa já tem os seus
// testes de fronteira em PaymentApprovalRuleTests. O que se testa é o contrato
// da porta: recusa volta como resultado, indisponibilidade sobe como exceção.
// É essa separação que a política de retry da T-014 vai usar.
public class SimulatedPaymentGatewayTests
{
    private static SimulatedPaymentGateway GatewayWith(bool unavailable = false) =>
        new(Options.Create(new SimulatedPaymentGatewayOptions { Unavailable = unavailable }),
            NullLogger<SimulatedPaymentGateway>.Instance);

    private static PaymentChargeRequest RequestOf(decimal amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), amount);

    [Theory] // RN-10, CA-6, CA-8
    [InlineData(500.00)]
    [InlineData(10_000.00)] // o limite exato aprova
    public async Task Aprova_cobranca_ate_o_limite(decimal amount)
    {
        var result = await GatewayWith().ChargeAsync(RequestOf(amount), CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.DeclineReason.Should().BeNull();
    }

    [Fact] // RN-10, CA-7
    public async Task Recusa_cobranca_acima_do_limite_com_motivo()
    {
        var result = await GatewayWith().ChargeAsync(RequestOf(10_000.01m), CancellationToken.None);

        result.Approved.Should().BeFalse();
        result.DeclineReason.Should().Be(PaymentApprovalRule.DeclineReasonFor(10_000.01m));
    }

    [Fact] // FA-2 — recusa é resposta, não falha: nada deve subir daqui
    public async Task Recusa_nao_lanca_excecao()
    {
        var charge = async () =>
            await GatewayWith().ChargeAsync(RequestOf(50_000.00m), CancellationToken.None);

        await charge.Should().NotThrowAsync();
    }

    [Fact] // RN-11, R-3 — o modo que existe para exercitar a falha de propósito
    public async Task Modo_indisponivel_lanca_excecao_de_gateway()
    {
        var charge = async () =>
            await GatewayWith(unavailable: true).ChargeAsync(RequestOf(500.00m), CancellationToken.None);

        await charge.Should().ThrowAsync<PaymentGatewayUnavailableException>();
    }

    [Fact] // RN-11 — indisponível é indisponível, mesmo para valor que aprovaria
    public async Task Modo_indisponivel_nao_avalia_o_valor()
    {
        var charge = async () =>
            await GatewayWith(unavailable: true).ChargeAsync(RequestOf(0.01m), CancellationToken.None);

        await charge.Should().ThrowAsync<PaymentGatewayUnavailableException>();
    }

    [Fact] // RN-10 — determinismo: a demonstração ao vivo não pode depender de sorte
    public async Task Mesma_cobranca_produz_sempre_o_mesmo_desfecho()
    {
        var gateway = GatewayWith();
        var request = RequestOf(10_000.01m);

        var first = await gateway.ChargeAsync(request, CancellationToken.None);
        var second = await gateway.ChargeAsync(request, CancellationToken.None);

        second.Should().Be(first);
    }
}
