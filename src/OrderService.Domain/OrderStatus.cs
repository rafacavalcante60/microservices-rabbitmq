namespace OrderService.Domain;

public enum OrderStatus
{
    Pending,
    Paid,
    PaymentDeclined,
    PaymentFailed
}
