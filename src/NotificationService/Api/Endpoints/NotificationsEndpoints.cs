using NotificationService.Application.GetNotifications;

namespace NotificationService.Api.Endpoints;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        // `:guid` na rota pelo mesmo motivo do OrderService: id malformado vira
        // 404 antes de chegar ao handler, em vez de explodir na conversão.
        app.MapGet("/notifications/order/{orderId:guid}", GetByOrderAsync)
            .WithName("GetNotificationsByOrder");

        return app;
    }

    private static async Task<IResult> GetByOrderAsync(
        Guid orderId,
        GetNotificationsHandler handler,
        CancellationToken cancellationToken)
    {
        var notifications = await handler.HandleAsync(orderId, cancellationToken);

        // Lista vazia com 200, e **não** 404. A diferença importa aqui mais que
        // no GET /orders: o sistema é eventualmente consistente, então existe
        // uma janela de milissegundos em que o pedido já foi pago e a
        // notificação ainda não chegou. Um 404 nessa janela diria "não existe"
        // sobre algo que está a caminho — e quem consulta trataria como erro
        // aquilo que é o funcionamento normal do fluxo assíncrono.
        //
        // A pergunta que esta rota responde é "quais notificações existem para
        // este pedido", e "nenhuma ainda" é uma resposta válida, não uma falha.
        return Results.Ok(notifications);
    }
}
