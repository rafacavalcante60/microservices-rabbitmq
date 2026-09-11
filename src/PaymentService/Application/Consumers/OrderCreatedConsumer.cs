using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Application.ChargeOrder;
using PaymentService.Domain;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Persistence.Configurations;
using Shared.Contracts;

// O MassTransit também tem um LogContext, e o nome colide. O apelido deixa
// explícito qual dos dois é o do Serilog.
using SerilogContext = Serilog.Context.LogContext;

namespace PaymentService.Application.Consumers;

// O coração do PaymentService: recebe o fato "pedido criado", cobra, registra e
// anuncia o desfecho. Tudo o que protege esse caminho já foi construído nas
// tarefas anteriores e apenas se encontra aqui — idempotência (T-016), retry do
// gateway (T-014), constraint única (T-015), outbox (esta tarefa).
public class OrderCreatedConsumer(
    ChargeOrderHandler chargeOrder,
    PaymentsDbContext dbContext,
    IPublishEndpoint publishEndpoint,
    ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var message = context.Message;

        // Princípio VII. O CorrelationId veio no evento, nascido do outro lado:
        // a partir daqui todo log deste consumo sai carimbado com ele, e o
        // fluxo dos três serviços pode ser lido como uma coisa só.
        using var _ = SerilogContext.PushProperty("CorrelationId", message.CorrelationId);

        logger.LogInformation(
            "Cobrando pedido {OrderId} no valor de {Amount}.", message.OrderId, message.TotalAmount);

        var payment = await chargeOrder.HandleAsync(
            new PaymentChargeRequest(message.OrderId, message.CustomerId, message.TotalAmount),
            context.CancellationToken);

        dbContext.Payments.Add(payment);

        // Como no OrderService, este Publish não fala com o broker: o
        // UseBusOutbox grava o evento na tabela outbox_message deste mesmo
        // contexto. A cobrança e o anúncio dela entram ou saem juntos.
        var outcome = OutcomeEventFor(payment, message.CorrelationId);

        // A sobrecarga com `Type` é obrigatória aqui: `Publish(object)` fecharia
        // o genérico em `System.Object` e a mensagem seria publicada num tópico
        // que ninguém assina — some sem erro nenhum, que é o pior tipo de bug.
        await publishEndpoint.Publish(outcome, outcome.GetType(), context.CancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateOrder(exception))
        {
            // RN-9 / CA-10. Chegar aqui significa que a checagem no Redis não
            // pegou a duplicata — porque o Redis estava fora (R-4, falha
            // aberta) ou porque duas instâncias processaram a mesma mensagem no
            // mesmo instante. É exatamente o caso para o qual a constraint
            // existe: o banco recusa, a transação inteira é desfeita — inclusive
            // a linha do outbox — e nenhum segundo desfecho é publicado.
            //
            // Note o que **não** se faz aqui: lançar. A cobrança já aconteceu e
            // já foi anunciada na primeira vez. Mandar esta mensagem para a DLQ
            // transformaria a proteção funcionando em incidente para alguém
            // investigar de madrugada.
            logger.LogWarning(
                "Pedido {OrderId} já possuía pagamento registrado; duplicata descartada pelo banco.",
                message.OrderId);

            return;
        }

        logger.LogInformation(
            "Pagamento do pedido {OrderId} concluído como {Status} em {Attempts} tentativa(s).",
            message.OrderId,
            payment.Status,
            payment.Attempts);
    }

    // Um desfecho, um evento. São três contratos distintos e não um só com
    // campo `status` porque cada consumidor assina o que lhe interessa: o
    // NotificationService monta mensagens diferentes, e um consumidor futuro de
    // antifraude assinaria só o recusado, sem receber e descartar os outros.
    private static object OutcomeEventFor(Payment payment, Guid correlationId) => payment.Status switch
    {
        PaymentStatus.Approved => new PaymentApproved(
            payment.Id, payment.OrderId, payment.CustomerId, payment.Amount,
            DateTime.UtcNow, correlationId),

        PaymentStatus.Declined => new PaymentDeclined(
            payment.Id, payment.OrderId, payment.CustomerId, payment.Amount, payment.Reason!,
            DateTime.UtcNow, correlationId),

        PaymentStatus.Failed => new PaymentFailed(
            payment.Id, payment.OrderId, payment.CustomerId, payment.Amount, payment.Reason!,
            payment.Attempts, DateTime.UtcNow, correlationId),

        _ => throw new InvalidOperationException($"Desfecho de pagamento desconhecido: {payment.Status}.")
    };

    // Só a violação *deste* índice é cobrança repetida. Qualquer outra violação
    // de unicidade é defeito e continua subindo — vira retry e, persistindo,
    // DLQ. Engolir todo 23505 esconderia bug futuro atrás de um log amarelo.
    private static bool IsDuplicateOrder(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PaymentConfiguration.UniqueOrderIndexName
        };
}
