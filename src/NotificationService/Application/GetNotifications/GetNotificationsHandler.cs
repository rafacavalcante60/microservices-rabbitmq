using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Application.GetNotifications;

public class GetNotificationsHandler(NotificationStore store)
{
    public async Task<IReadOnlyList<NotificationResponse>> HandleAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var notifications = await store.GetByOrderAsync(orderId, cancellationToken);

        return notifications.Select(notification => new NotificationResponse(
            notification.OrderId,
            notification.CustomerId,
            notification.Type,
            notification.Message,
            notification.Reason,
            notification.CreatedAt,
            notification.CorrelationId)).ToList();
    }
}
