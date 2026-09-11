namespace PaymentService.Infrastructure;

public sealed class SimulatedPaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    // Liga o modo "processador fora do ar" (RN-11, R-3): toda cobrança passa a
    // não responder. Existe para que a indisponibilidade seja demonstrável sem
    // recompilar nada — basta a variável de ambiente
    // `PaymentGateway__Unavailable=true` no docker compose — e para que o
    // caminho de falha seja exercitado de propósito, e não só no dia em que
    // acontecer de verdade.
    public bool Unavailable { get; set; }
}
