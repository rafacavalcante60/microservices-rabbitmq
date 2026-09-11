using PaymentService.Domain;
using PaymentService.Infrastructure;

namespace Integration.Tests.Fixtures;

// O gateway simulado (D-8) decide por valor: ele não tem como "cair" para um
// pedido e responder para outro, e o interruptor `PaymentGateway__Unavailable`
// é global e lido na construção — derruba o serviço inteiro, não uma cobrança.
// CA-13 e CA-14 precisam exatamente disso: indisponibilidade por pedido, com
// contagem de tentativas, enquanto os outros testes da mesma coleção continuam
// usando o gateway de verdade.
//
// Por isso este dublê **decora** o simulador em vez de substituí-lo: sem roteiro
// para o pedido, a chamada passa direto para o componente de produção. O que os
// testes de fluxo exercitam continua sendo o código real.
public sealed class ScriptedPaymentGateway(SimulatedPaymentGateway inner) : IPaymentGateway
{
    private readonly Dictionary<Guid, Roteiro> _roteiros = [];
    private readonly object _gate = new();

    // O gateway nunca responde para este pedido, por mais que insistam (CA-13).
    public void NuncaResponde(Guid orderId) => Escrever(orderId, int.MaxValue);

    // Falha as primeiras `falhas` tentativas e depois se comporta normalmente
    // (CA-14) — a indisponibilidade passageira, que é o caso comum de verdade.
    public void FalhaAntesDeResponder(Guid orderId, int falhas) => Escrever(orderId, falhas);

    // Quantas vezes o gateway foi chamado para este pedido. É o número que
    // prende RN-11 no lugar: "até 5 tentativas" só é verificável contando as
    // idas ao gateway — o status final sozinho não distingue 5 de 50.
    public int TentativasDe(Guid orderId)
    {
        lock (_gate)
        {
            return _roteiros.TryGetValue(orderId, out var roteiro) ? roteiro.Tentativas : 0;
        }
    }

    public Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request, CancellationToken cancellationToken)
    {
        bool indisponivel;

        lock (_gate)
        {
            if (!_roteiros.TryGetValue(request.OrderId, out var roteiro))
            {
                return inner.ChargeAsync(request, cancellationToken);
            }

            roteiro.Tentativas++;
            indisponivel = roteiro.Tentativas <= roteiro.FalhasPrevistas;
        }

        if (indisponivel)
        {
            // A mesma exceção do simulador, e não uma qualquer: é o tipo que a
            // política do Polly retenta (ShouldHandle em ChargeOrderHandler).
            // Lançar outra coisa transformaria este teste num teste de outro
            // caminho — o da exceção inesperada, que vai para a DLQ.
            throw new PaymentGatewayUnavailableException(
                $"Roteiro de teste: processador indisponível para o pedido {request.OrderId}.");
        }

        return inner.ChargeAsync(request, cancellationToken);
    }

    private void Escrever(Guid orderId, int falhas)
    {
        lock (_gate)
        {
            _roteiros[orderId] = new Roteiro(falhas);
        }
    }

    // O consumidor roda em thread do barramento e o teste lê da thread dele;
    // sem o lock, a contagem de tentativas seria uma corrida.
    private sealed class Roteiro(int falhasPrevistas)
    {
        public int FalhasPrevistas { get; } = falhasPrevistas;

        public int Tentativas { get; set; }
    }
}
