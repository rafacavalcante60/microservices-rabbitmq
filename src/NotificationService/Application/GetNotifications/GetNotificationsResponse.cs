namespace NotificationService.Application.GetNotifications;

// RN-13: a notificação guarda a qual pedido se refere, a qual cliente, o
// resultado comunicado e o instante do registro. Esta resposta é exatamente
// esse contrato — o `_id` do Mongo não aparece porque é identidade de
// armazenamento, não informação para quem consulta.
public record NotificationResponse(
    Guid OrderId,
    Guid CustomerId,
    string Type,
    string Message,
    string? Reason,
    DateTime CreatedAt,
    Guid CorrelationId);
