using MongoDB.Driver;

namespace NotificationService.Infrastructure.Persistence;

// Resultado da tentativa de registrar. `Duplicate` não é falha: é o índice
// único fazendo o trabalho dele. Devolver um enum em vez de deixar a exceção
// subir mantém a decisão de "isso é sucesso" num lugar só.
public enum InsertOutcome
{
    Inserted,
    Duplicate
}

public class NotificationStore(IMongoCollection<Notification> collection)
{
    public async Task<InsertOutcome> InsertAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await collection.InsertOneAsync(notification, options: null, cancellationToken);

            return InsertOutcome.Inserted;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // RN-12 / CA-10. O Redis já teria barrado a reentrega na maioria dos
            // casos (T-016), mas ele falha aberto quando está fora (R-4) e duas
            // instâncias podem processar a mesma mensagem no mesmo instante. O
            // índice único é a garantia que não depende de cache.
            //
            // Tratar como sucesso é o ponto da tarefa: o estado desejado —
            // exatamente uma notificação para este pedido — já é verdade. Deixar
            // a exceção subir mandaria a mensagem para a DLQ e transformaria a
            // proteção funcionando em incidente para alguém investigar.
            return InsertOutcome.Duplicate;
        }
    }

    // A consulta é por `orderId`, que já tem índice único — então ela usa o
    // mesmo índice que existe para garantir RN-12. Não foi preciso criar índice
    // nenhum para esta leitura.
    //
    // Devolve lista e não um único documento mesmo o índice garantindo no
    // máximo um: a rota é `/notifications/order/{id}`, um recurso de coleção, e
    // uma coleção que às vezes devolve objeto e às vezes lista é API que obriga
    // o cliente a testar o tipo. Se um dia um pedido gerar notificação por mais
    // de um canal, o contrato não muda.
    public Task<List<Notification>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        collection.Find(candidate => candidate.OrderId == orderId).ToListAsync(cancellationToken);
}
