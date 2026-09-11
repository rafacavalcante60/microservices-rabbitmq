namespace PaymentService.Domain;

public class Payment
{
    // Construtor do ORM — o EF materializa e preenche os campos depois, sem
    // passar pelas validações. Uma linha que já está no banco foi validada
    // quando nasceu. Fica privado para que só o EF o alcance.
    private Payment()
    {
    }

    // Privado de propósito: não existe "criar um Payment e depois decidir o
    // desfecho". O desfecho é o motivo de o registro existir, então ele entra
    // pelo construtor e as três fábricas abaixo são as únicas portas.
    private Payment(
        Guid orderId,
        Guid customerId,
        decimal amount,
        PaymentStatus status,
        string? reason,
        int attempts)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Pagamento precisa de um pedido.", nameof(orderId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Pagamento precisa de um cliente.", nameof(customerId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Valor precisa ser maior que zero.");
        }

        // Uma cobrança sem nenhuma tentativa não aconteceu. O contador existe
        // para o operador saber se o desfecho veio de primeira ou depois de
        // cinco idas ao gateway (RN-11) — informação que some se ninguém contar.
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Cobrança tem ao menos uma tentativa.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Status = status;
        Reason = reason;
        Attempts = attempts;

        // Os dois instantes coincidem porque o registro só nasce quando o
        // desfecho já é conhecido. As colunas são separadas no modelo porque
        // numa integração real haveria intervalo entre pedir e concluir a
        // cobrança, e essa distinção é o que se mede quando o gateway degrada.
        CreatedAt = DateTimeOffset.UtcNow;
        CompletedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // Só desfecho negativo tem motivo. Aprovado não precisa explicar-se.
    public string? Reason { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public static Payment Approved(Guid orderId, Guid customerId, decimal amount, int attempts) =>
        new(orderId, customerId, amount, PaymentStatus.Approved, reason: null, attempts);

    // Recusado: o processador respondeu "não". É decisão de negócio (FA-2).
    public static Payment Declined(Guid orderId, Guid customerId, decimal amount, string reason, int attempts) =>
        new(orderId, customerId, amount, PaymentStatus.Declined, RequireReason(reason), attempts);

    // Falhou: o processador não respondeu. É problema técnico (FA-3). A spec
    // separa os dois porque a ação de suporte é diferente — recusado encerra o
    // pedido, falhou pede investigação.
    public static Payment Failed(Guid orderId, Guid customerId, decimal amount, string reason, int attempts) =>
        new(orderId, customerId, amount, PaymentStatus.Failed, RequireReason(reason), attempts);

    // RN-10 / CA-7 / CA-8. O agregado decide a partir da regra em vez de
    // receber o desfecho pronto: quem chama não consegue gravar um pagamento
    // de R$ 50.000,00 como aprovado por engano.
    public static Payment For(Guid orderId, Guid customerId, decimal amount, int attempts) =>
        PaymentApprovalRule.Approves(amount)
            ? Approved(orderId, customerId, amount, attempts)
            : Declined(orderId, customerId, amount, PaymentApprovalRule.DeclineReasonFor(amount), attempts);

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Desfecho negativo precisa de motivo.", nameof(reason));
        }

        return reason;
    }
}
