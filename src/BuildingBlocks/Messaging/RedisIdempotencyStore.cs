using StackExchange.Redis;

namespace BuildingBlocks.Messaging;

// D-4. `SET chave valor NX EX 604800` numa única ida ao Redis:
//
//   NX — grava só se a chave não existir. O retorno diz se gravou, e é esse
//        booleano que responde "já vi esta mensagem?". Teste e marcação no
//        mesmo comando, sem janela entre os dois.
//   EX — expira em 7 dias. Sem isso o Redis cresceria para sempre guardando
//        identificadores de mensagens que ninguém vai reentregar. Sete dias é
//        folgado para qualquer reentrega de broker e barato de manter.
public class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);

    public static string KeyFor(string consumer, Guid messageId) => $"idem:{consumer}:{messageId}";

    // O CancellationToken não é repassado porque o StackExchange.Redis não o
    // aceita — ele controla tempo por timeout de conexão, não por token. Fica
    // na assinatura mesmo assim: é a convenção do projeto, e o dia em que a
    // implementação mudar para outra biblioteca o parâmetro já está lá.
    public async Task<bool> TryMarkAsProcessedAsync(
        string consumer, Guid messageId, CancellationToken cancellationToken)
    {
        return await redis.GetDatabase().StringSetAsync(
            KeyFor(consumer, messageId),
            DateTimeOffset.UtcNow.ToString("O"),
            RetentionWindow,
            When.NotExists);
    }
}
