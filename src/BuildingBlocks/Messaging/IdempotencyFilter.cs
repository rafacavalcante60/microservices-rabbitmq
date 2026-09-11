using MassTransit;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

// Princípio IV. O RabbitMQ entrega *pelo menos uma vez*: se o `ack` se perder na
// rede, ou o consumidor morrer entre processar e confirmar, a mesma mensagem
// chega de novo. Não é hipótese remota — é a garantia operacional do broker, e
// o outbox (princípio V) reforça isso, porque o publicador também pode repetir.
//
// Sem esta proteção, um `OrderCreated` reentregue viraria uma segunda cobrança
// no cartão do cliente. O filtro roda antes do consumidor, no pipeline do
// MassTransit, então nenhum consumidor precisa lembrar de se proteger — é o
// tipo de regra que não pode depender de disciplina de quem escreve o próximo.
public class IdempotencyFilter<T>(IIdempotencyStore store, ILogger<IdempotencyFilter<T>> logger)
    : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var messageId = context.MessageId;

        // Mensagem sem MessageId não tem como ser reconhecida na segunda vez.
        // Processar é o mal menor: recusar descartaria trabalho legítimo. O
        // MassTransit sempre gera um, então isto aqui é rede de proteção para
        // mensagem publicada por outra stack.
        if (messageId is null)
        {
            logger.LogWarning(
                "Mensagem {MessageType} chegou sem MessageId; seguindo sem checagem de duplicata.",
                typeof(T).Name);

            await next.Send(context);
            return;
        }

        var consumer = ConsumerNameFor(context);

        bool firstTime;

        try
        {
            firstTime = await store.TryMarkAsProcessedAsync(consumer, messageId.Value, context.CancellationToken);
        }
        catch (Exception exception)
        {
            // R-4: **falha aberta**. Com o Redis fora, processar é melhor que
            // parar: as restrições únicas em `payments.order_id` e
            // `notifications.orderId` seguram a correção no banco, que é a
            // garantia que não depende de cache. Falhar fechado pararia o
            // sistema inteiro por causa de uma otimização.
            //
            // O warning existe porque "está funcionando com a rede de
            // segurança rasgada" precisa aparecer em algum lugar.
            logger.LogWarning(
                exception,
                "Checagem de idempotência indisponível para {MessageType} {MessageId}; processando mesmo assim.",
                typeof(T).Name,
                messageId);

            await next.Send(context);
            return;
        }

        if (!firstTime)
        {
            // Descartada **e confirmada**: não chamar `next` encerra o
            // pipeline sem erro, e o MassTransit dá `ack`. Lançar exceção aqui
            // mandaria a duplicata para a DLQ, tratando rotina como incidente.
            logger.LogInformation(
                "Mensagem {MessageType} {MessageId} já processada por {Consumer}; descartada.",
                typeof(T).Name,
                messageId,
                consumer);

            return;
        }

        // A marcação acontece **antes** do processamento, como manda o princípio
        // IV. O risco R-5 é conhecido e aceito: se o processo morrer entre
        // marcar e commitar no banco, a reentrega é descartada como duplicata e
        // o efeito se perde. A janela é de milissegundos, e o inverso — marcar
        // depois — deixaria a duplicata passar sempre que o `ack` falhasse,
        // que é o caso comum. Trocar aqui por uma inbox transacional é a saída
        // definitiva; está registrada em D-4 como alternativa descartada.
        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("idempotency");

    // A chave é por **consumidor**, não só por mensagem, e isso é essencial: o
    // mesmo `PaymentApproved` é consumido pelo OrderService e pelo
    // NotificationService, com o mesmo MessageId, num Redis compartilhado. Uma
    // chave só por mensagem faria o segundo serviço descartar um evento que ele
    // nunca viu — e a notificação simplesmente não existiria.
    //
    // O nome da fila é o discriminador certo porque é exatamente o que
    // identifica um ponto de consumo no broker.
    private static string ConsumerNameFor(ConsumeContext<T> context)
    {
        var queue = context.ReceiveContext.InputAddress.AbsolutePath.Trim('/');

        return string.IsNullOrEmpty(queue) ? typeof(T).Name : queue;
    }
}
