using Microsoft.Extensions.Options;
using PaymentService.Domain;

namespace PaymentService.Infrastructure;

// D-8. O processador simulado. Não há rede, não há credencial, não há sandbox
// externo: a decisão sai de RN-10, aplicada pela mesma PaymentApprovalRule que
// o domínio usa. Uma segunda cópia da regra aqui seria a receita para o
// simulador aprovar o que o agregado recusa.
public sealed class SimulatedPaymentGateway(
    IOptions<SimulatedPaymentGatewayOptions> options,
    ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway
{
    private readonly SimulatedPaymentGatewayOptions _options = options.Value;

    public Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.Unavailable)
        {
            // Warning e não Error: aqui a indisponibilidade foi pedida por
            // configuração. Quem decide que virou incidente é a T-014, depois
            // de esgotar as tentativas.
            logger.LogWarning(
                "Gateway simulado em modo indisponível; cobrança do pedido {OrderId} não foi respondida.",
                request.OrderId);

            throw new PaymentGatewayUnavailableException(
                $"Processador de pagamento indisponível para o pedido {request.OrderId}.");
        }

        var result = PaymentApprovalRule.Approves(request.Amount)
            ? PaymentChargeResult.Approve()
            : PaymentChargeResult.Decline(PaymentApprovalRule.DeclineReasonFor(request.Amount));

        // Síncrono por dentro, assíncrono no contrato: a assinatura é a de uma
        // chamada de rede porque é isso que ela vai ser quando o simulador sair.
        // Mudar a assinatura depois obrigaria a mexer no caso de uso e nos
        // testes — exatamente o acoplamento que a abstração existe para evitar.
        return Task.FromResult(result);
    }
}
