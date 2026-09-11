using System.Globalization;
using MassTransit;
using NotificationService.Infrastructure.Persistence;

using SerilogContext = Serilog.Context.LogContext;

namespace NotificationService.Application.Consumers;

// O mesmo desenho do OrderService: os três desfechos chegam em contratos
// distintos, mas o trabalho é um só — montar o documento e inserir. Cada
// consumidor concreto responde apenas "que documento este desfecho vira".
//
// Este serviço consome os **mesmos** eventos que o OrderService, em paralelo e
// sem saber que ele existe. É o ponto da mensageria: adicionar um consumidor
// novo não toca em nenhuma linha do publicador. Se este serviço cair, pedidos
// continuam sendo pagos — as notificações ficam na fila e entram quando voltar.
public abstract class NotificationConsumer<TOutcome>(
    NotificationStore store,
    ILogger logger) : IConsumer<TOutcome>
    where TOutcome : class
{
    public async Task Consume(ConsumeContext<TOutcome> context)
    {
        var notification = Build(context.Message);

        using var _ = SerilogContext.PushProperty("CorrelationId", notification.CorrelationId);

        var outcome = await store.InsertAsync(notification, context.CancellationToken);

        if (outcome == InsertOutcome.Duplicate)
        {
            logger.LogInformation(
                "Pedido {OrderId} já tinha notificação registrada; duplicata descartada pelo índice único.",
                notification.OrderId);

            return;
        }

        logger.LogInformation(
            "Notificação {Type} registrada para o pedido {OrderId}.",
            notification.Type,
            notification.OrderId);
    }

    protected abstract Notification Build(TOutcome message);

    // Cultura explícita pelo mesmo motivo do PaymentApprovalRule: a mensagem
    // fica gravada no documento e é o que o cliente lê. Moeda que troca de
    // separador conforme o locale do contêiner que atendeu é defeito.
    private static readonly CultureInfo Brazilian = CultureInfo.GetCultureInfo("pt-BR");

    protected static string Money(decimal amount) =>
        string.Format(Brazilian, "R$ {0:N2}", amount);
}
