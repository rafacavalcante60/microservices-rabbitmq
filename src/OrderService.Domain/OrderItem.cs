namespace OrderService.Domain;

public class OrderItem
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 100;

    public OrderItem(Guid productId, string productName, int quantity, decimal unitPrice)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Item precisa de um produto.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new ArgumentException("Item precisa do nome do produto.", nameof(productName));
        }

        // RN-2
        if (quantity is < MinQuantity or > MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"Quantidade precisa estar entre {MinQuantity} e {MaxQuantity}.");
        }

        // RN-3
        if (unitPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Preço unitário precisa ser maior que zero.");
        }

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
