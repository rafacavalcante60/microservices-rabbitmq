using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NotificationService.Infrastructure.Persistence;

// Documento, não agregado. O NotificationService não tem regra de negócio para
// proteger — ele registra um fato que já foi decidido em outro serviço — então
// não existe projeto de domínio separado aqui (ver plan.md §Estrutura). Os
// atributos do driver ficam no próprio tipo porque não há fronteira interna
// que eles pudessem sujar.
public class Notification
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.String)]
    public Guid OrderId { get; set; }

    [BsonElement("customerId")]
    [BsonRepresentation(BsonType.String)]
    public Guid CustomerId { get; set; }

    [BsonElement("type")]
    public string Type { get; set; } = null!;

    [BsonElement("message")]
    public string Message { get; set; } = null!;

    // Nulo no desfecho positivo. Num documento, campo ausente não custa coluna
    // nem default — é o tipo de flexibilidade que motivou escolher Mongo aqui.
    [BsonElement("reason")]
    [BsonIgnoreIfNull]
    public string? Reason { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("correlationId")]
    [BsonRepresentation(BsonType.String)]
    public Guid CorrelationId { get; set; }
}

// Os três valores previstos em plan.md §NotificationService. Constantes e não
// enum: o valor vai para o documento como texto, e um enum gravado como número
// tornaria a coleção ilegível no shell do Mongo — que é onde se olha quando
// algo dá errado.
public static class NotificationType
{
    public const string OrderPaid = "OrderPaid";
    public const string PaymentDeclined = "PaymentDeclined";
    public const string PaymentFailed = "PaymentFailed";
}
