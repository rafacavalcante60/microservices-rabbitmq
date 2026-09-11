using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;

// O MassTransit também tem um LogContext, e o nome colide. O apelido deixa
// explícito qual dos dois é o do Serilog.
using SerilogContext = Serilog.Context.LogContext;

namespace OrderService.Application.Consumers;

// Os três desfechos de pagamento chegam como contratos distintos (D-2), mas o
// que o OrderService faz com eles é o mesmo trabalho: achar o pedido, pedir a
// transição ao agregado, salvar. A base concentra esse caminho e cada
// consumidor concreto só responde a uma pergunta — qual transição e com que
// motivo. Repetir as vinte linhas três vezes convidaria as três cópias a
// divergirem na primeira correção feita com pressa.
public abstract class PaymentOutcomeConsumer<TOutcome>(
    OrdersDbContext dbContext,
    ILogger logger) : IConsumer<TOutcome>
    where TOutcome : class
{
    public async Task Consume(ConsumeContext<TOutcome> context)
    {
        var (orderId, correlationId) = Identify(context.Message);

        // Princípio VII. O CorrelationId nasceu na borda, atravessou o
        // PaymentService e volta aqui: os logs do salto inteiro se leem juntos.
        using var _ = SerilogContext.PushProperty("CorrelationId", correlationId);

        var order = await dbContext.Orders
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, context.CancellationToken);

        // Não deveria acontecer: o desfecho só existe porque houve um
        // OrderCreated, que por sua vez só foi publicado pelo outbox depois de
        // o pedido estar comitado. Se acontecer, é defeito — e defeito não se
        // engole. Lançar aciona o retry do consumidor e, persistindo, a DLQ
        // (princípio VI), com a mensagem preservada para reprocessar.
        if (order is null)
        {
            throw new InvalidOperationException(
                $"Desfecho de pagamento recebido para pedido inexistente: {orderId}.");
        }

        var statusBefore = order.Status;

        Apply(order, context.Message);

        // RN-7 / CA-9 / R-8. A guarda está no agregado: se o pedido já saiu de
        // Pending, a chamada acima não mudou nada e o SaveChanges abaixo não
        // emite UPDATE nenhum. O consumidor não repete a regra, só registra que
        // o desfecho chegou tarde — que com entrega at-least-once é rotina.
        if (order.Status == statusBefore)
        {
            logger.LogInformation(
                "Pedido {OrderId} já estava em {Status}; desfecho {Outcome} ignorado.",
                orderId,
                statusBefore,
                typeof(TOutcome).Name);

            return;
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Pedido {OrderId} transicionado de {PreviousStatus} para {Status}.",
            orderId,
            statusBefore,
            order.Status);
    }

    protected abstract (Guid OrderId, Guid CorrelationId) Identify(TOutcome message);

    protected abstract void Apply(Order order, TOutcome message);
}
