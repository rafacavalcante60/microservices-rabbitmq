namespace OrderService.Domain;

public class Order
{
    public const string DefaultCurrency = "BRL";

    private readonly List<OrderItem> _items;

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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
}
