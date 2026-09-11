namespace BuildingBlocks.Messaging;

// A porta que o filtro usa para lembrar o que já processou. Existe como
// interface por dois motivos: o filtro fica testável sem Redis, e a política de
// falha (o que fazer quando o armazenamento some) fica no filtro, separada do
// mecanismo — ver IdempotencyFilter.
public interface IIdempotencyStore
{
    // Devolve `true` quando esta mensagem foi marcada agora — ou seja, é a
    // primeira vez que ela aparece. `false` significa que alguém já a marcou.
    //
    // A operação é **uma só**, atômica: testar e depois marcar em duas idas ao
    // Redis teria uma janela em que duas instâncias leem "não vi" e ambas
    // processam. É o mesmo raciocínio do índice único em `orders` (D-5).
    Task<bool> TryMarkAsProcessedAsync(string consumer, Guid messageId, CancellationToken cancellationToken);
}
