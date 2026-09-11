using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentService.Application.ChargeOrder;

namespace PaymentService.Domain.Tests;

// D-6 / RN-11. O gateway falso é o ponto: a política precisa ser exercitada sem
// rede e sem espera real. Todo teste aqui roda com BaseDelay de 1ms — o que se
// verifica é a **contagem** de idas ao gateway e o desfecho, não o relógio.
public class RetryPolicyTests
{
    private static ChargeOrderHandler HandlerFor(IPaymentGateway gateway, int maxAttempts = 5) =>
        new(gateway,
            Options.Create(new PaymentRetryOptions
            {
                MaxAttempts = maxAttempts,
                BaseDelay = TimeSpan.FromMilliseconds(1)
            }),
            NullLogger<ChargeOrderHandler>.Instance);

    private static PaymentChargeRequest RequestOf(decimal amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), amount);

    [Fact] // CA-13 — esgotadas as tentativas, o desfecho é Failed
    public async Task Gateway_que_nunca_responde_termina_em_failed()
    {
        var gateway = FakeGateway.AlwaysUnavailable();

        var payment = await HandlerFor(gateway).HandleAsync(RequestOf(500.00m), CancellationToken.None);

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.Reason.Should().NotBeNullOrWhiteSpace();

        // A contagem é o coração do teste: 5 tentativas são 5 chamadas, não 6.
        // Trocar MaxRetryAttempts por MaxAttempts no handler quebra aqui.
        gateway.Calls.Should().Be(5);
        payment.Attempts.Should().Be(5);
    }

    [Fact] // CA-14 — recuperou antes de esgotar, segue o fluxo normal
    public async Task Gateway_que_falha_duas_vezes_e_aprova_na_terceira_termina_em_approved()
    {
        var gateway = FakeGateway.UnavailableForFirst(2);

        var payment = await HandlerFor(gateway).HandleAsync(RequestOf(500.00m), CancellationToken.None);

        payment.Status.Should().Be(PaymentStatus.Approved);
        payment.Reason.Should().BeNull();
        gateway.Calls.Should().Be(3);
        payment.Attempts.Should().Be(3);
    }

    [Fact] // FA-2 vs FA-3 — recusa não é falha, e por isso não é retentada
    public async Task Recusa_nao_e_retentada()
    {
        var gateway = FakeGateway.AlwaysAvailable();

        var payment = await HandlerFor(gateway).HandleAsync(RequestOf(10_000.01m), CancellationToken.None);

        payment.Status.Should().Be(PaymentStatus.Declined);
        payment.Reason.Should().Be(PaymentApprovalRule.DeclineReasonFor(10_000.01m));

        // Uma única ida ao gateway. Se a política tratasse recusa como falha,
        // seriam cinco — e num gateway real, cinco riscos de cobrança dupla.
        gateway.Calls.Should().Be(1);
        payment.Attempts.Should().Be(1);
    }

    [Fact] // CA-6 — caminho feliz não paga o custo da política
    public async Task Aprovacao_de_primeira_conta_uma_tentativa()
    {
        var gateway = FakeGateway.AlwaysAvailable();

        var payment = await HandlerFor(gateway).HandleAsync(RequestOf(500.00m), CancellationToken.None);

        payment.Status.Should().Be(PaymentStatus.Approved);
        gateway.Calls.Should().Be(1);
        payment.Attempts.Should().Be(1);
    }

    [Fact] // RN-11 — o número de tentativas é configuração, não constante escondida
    public async Task Numero_de_tentativas_vem_da_configuracao()
    {
        var gateway = FakeGateway.AlwaysUnavailable();

        var payment = await HandlerFor(gateway, maxAttempts: 3)
            .HandleAsync(RequestOf(500.00m), CancellationToken.None);

        payment.Status.Should().Be(PaymentStatus.Failed);
        gateway.Calls.Should().Be(3);
    }

    [Fact] // A falha do gateway não escapa: quem chama recebe desfecho, não exceção
    public async Task Falha_do_gateway_nao_propaga_excecao()
    {
        var charge = async () =>
            await HandlerFor(FakeGateway.AlwaysUnavailable())
                .HandleAsync(RequestOf(500.00m), CancellationToken.None);

        await charge.Should().NotThrowAsync();
    }

    // Gateway falso com script: falha nas `unavailableCalls` primeiras chamadas
    // e depois aplica RN-10, como o simulador de verdade. Conta as idas, que é
    // o que os testes acima verificam.
    private sealed class FakeGateway(int unavailableCalls) : IPaymentGateway
    {
        public int Calls { get; private set; }

        public static FakeGateway AlwaysAvailable() => new(0);

        public static FakeGateway AlwaysUnavailable() => new(int.MaxValue);

        public static FakeGateway UnavailableForFirst(int calls) => new(calls);

        public Task<PaymentChargeResult> ChargeAsync(
            PaymentChargeRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            if (Calls <= unavailableCalls)
            {
                throw new PaymentGatewayUnavailableException("Processador não respondeu.");
            }

            var result = PaymentApprovalRule.Approves(request.Amount)
                ? PaymentChargeResult.Approve()
                : PaymentChargeResult.Decline(PaymentApprovalRule.DeclineReasonFor(request.Amount));

            return Task.FromResult(result);
        }
    }
}
