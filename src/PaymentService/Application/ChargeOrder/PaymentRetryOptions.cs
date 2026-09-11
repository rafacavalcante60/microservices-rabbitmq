namespace PaymentService.Application.ChargeOrder;

public sealed class PaymentRetryOptions
{
    public const string SectionName = "PaymentRetry";

    // RN-11: "até 5 tentativas". Tentativas, não retentativas — a primeira
    // chamada conta. É por isso que a política Polly é montada com
    // MaxRetryAttempts = MaxAttempts - 1; ver ChargeOrderHandler.
    public int MaxAttempts { get; set; } = 5;

    // Espera antes da 2ª tentativa; as seguintes dobram (500ms, 1s, 2s, 4s).
    // Configurável porque os testes precisam da mesma política sem os 7,5s de
    // espera — teste lento é teste que alguém acaba pulando.
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
