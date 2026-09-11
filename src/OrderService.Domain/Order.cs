namespace OrderService.Domain;

public class Order
{
    public const string DefaultCurrency = "BRL";

    private readonly List<OrderItem> _items;

    // Construtor do ORM. O EF Core materializa o objeto e depois preenche os
    // campos, sem passar pelas validações, e é isso que se quer: uma linha que
    // já está no banco foi validada quando nasceu. Fica privado para que só o
    // EF alcance; o resto do código só tem o construtor público, que valida.
    private Order()
    {
        _items = new List<OrderItem>();
        IdempotencyKey = null!;
        Currency = null!;
    }

    // Não existe parâmetro de total: RN-4 / CA-3 são garantidos pela ausência
    // dele, não por uma validação que alguém pode esquecer de chamar.
    public Order(Guid customerId, string idempotencyKey, IEnumerable<OrderItem> items)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Pedido precisa de um cliente.", nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Pedido precisa de uma chave de idempotência.", nameof(idempotencyKey));
        }

        ArgumentNullException.ThrowIfNull(items);

        var materialized = items.ToList();

        // RN-1
        if (materialized.Count == 0)
        {
            throw new ArgumentException("Pedido precisa de ao menos um item.", nameof(items));
        }

        if (materialized.Any(item => item is null))
        {
            throw new ArgumentException("Pedido não aceita item nulo.", nameof(items));
        }

        Id = Guid.NewGuid();
        CustomerId = customerId;
        IdempotencyKey = idempotencyKey;
        _items = materialized;
        TotalAmount = materialized.Sum(item => item.LineTotal); // RN-4
        Currency = DefaultCurrency;
        Status = OrderStatus.Pending; // RN-6
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string IdempotencyKey { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public void MarkAsPaid() => TransitionTo(OrderStatus.Paid, reason: null);

    public void MarkAsDeclined(string reason) => TransitionTo(OrderStatus.PaymentDeclined, RequireReason(reason));

    public void MarkAsFailed(string reason) => TransitionTo(OrderStatus.PaymentFailed, RequireReason(reason));

    // RN-7 / CA-9: a guarda vive aqui, e não no consumidor, porque a regra é do
    // agregado — qualquer caminho que alcance o pedido obedece a ela.
    // Ignorar em silêncio é deliberado: com entrega at-least-once, o desfecho
    // repetido ou atrasado é rotina do transporte, não erro de negócio. Lançar
    // exceção mandaria a mensagem para a DLQ e acionaria alarme por algo que o
    // sistema já tratou corretamente.
    private void TransitionTo(OrderStatus status, string? reason)
    {
        if (Status != OrderStatus.Pending)
        {
            return;
        }

        Status = status;
        StatusReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Desfecho negativo precisa de motivo.", nameof(reason));
        }

        return reason;
    }
}
