namespace PaymentService.Domain;

// D-8. A porta pela qual o caso de uso fala com o processador de pagamento. A
// interface vive no domínio e a implementação na infraestrutura, então a
// dependência aponta para dentro: trocar o simulador por uma integração real
// (Stripe, Cielo, o que for) não toca em uma linha do caso de uso.
//
// Num portfólio isto também compra o que a constituição cobra no princípio IX:
// dá para testar a regra de cobrança sem rede, sem credencial e sem contêiner.
public interface IPaymentGateway
{
    // Lança PaymentGatewayUnavailableException quando o processador não
    // respondeu. A distinção é o ponto central desta abstração e está explicada
    // na exceção, logo abaixo.
    Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken);
}

public sealed record PaymentChargeRequest(Guid OrderId, Guid CustomerId, decimal Amount);

// `Approved` e `DeclineReason` em vez de um enum de três valores: o resultado
// aqui só tem dois desfechos possíveis. "Falhou" não é um resultado — é a
// ausência de resultado, e por isso vira exceção, não campo.
public sealed record PaymentChargeResult(bool Approved, string? DeclineReason)
{
    public static PaymentChargeResult Approve() => new(true, null);

    public static PaymentChargeResult Decline(string reason) => new(false, reason);
}

// Por que indisponibilidade é exceção e recusa é valor de retorno:
//
// A spec separa recusado (FA-2) de falhou (FA-3), e o que separa os dois é se
// vale a pena tentar de novo. O processador que respondeu "não" vai responder
// "não" de novo — repetir é desperdício e, num gateway real, risco de cobrança
// dupla. O processador que não respondeu pode responder na próxima.
//
// Modelar assim faz a política de retry da T-014 cair naturalmente: o Polly
// repete em cima de exceção, e uma recusa simplesmente nunca chega a ele. Se
// os dois desfechos voltassem como campo do mesmo resultado, cada chamador
// precisaria lembrar de distinguir — e um dia alguém esqueceria.
public sealed class PaymentGatewayUnavailableException(string message) : Exception(message);
