using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Domain;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Configurations;
using Shared.Contracts;

namespace OrderService.Application.CreateOrder;

public class CreateOrderHandler(OrdersDbContext dbContext, IPublishEndpoint publishEndpoint)
{
    public async Task<CreateOrderResult> HandleAsync(
        CreateOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var items = request.Items!.Select(item => new OrderItem(
            item.ProductId,
            item.ProductName!,
            item.Quantity,
            item.UnitPrice));

        var order = new Order(request.CustomerId, idempotencyKey, items);

        dbContext.Orders.Add(order);

        // Princípio VII: o CorrelationId deveria nascer na borda, no gateway
        // (T-021). Enquanto o gateway não existe, ele nasce aqui — o importante
        // é que o evento nunca viaje sem um.
        var correlationId = Guid.NewGuid();

        // Este `Publish` não fala com o RabbitMQ. Por causa do `UseBusOutbox()`
        // configurado em T-007, o IPublishEndpoint deste escopo grava o evento
        // como uma linha na tabela outbox_message do próprio OrdersDbContext.
        // Nada sai daqui ainda.
        await publishEndpoint.Publish(
            new OrderCreated(
                order.Id,
                order.CustomerId,
                order.TotalAmount,
                order.Currency,
                DateTime.UtcNow,
                correlationId),
            cancellationToken);

        try
        {
            // É este SaveChanges que decide o destino dos dois: o pedido, os itens e
            // a linha do outbox entram na mesma transação. Ou tudo é gravado, ou
            // nada é — não existe o estado "pedido existe mas ninguém vai cobrá-lo".
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateIdempotencyKey(exception))
        {
            // D-5 / RN-8 / CA-11. A repetição não é tratada perguntando antes se
            // o pedido já existe: entre a pergunta e a inserção cabe outra
            // requisição, e o furo aconteceria justamente sob concorrência, que
            // é quando a idempotência importa. Aqui a inserção é sempre tentada
            // e o índice único é quem recusa — não há janela entre verificar e
            // agir porque não há verificação.
            //
            // Repare no efeito colateral feliz: a transação inteira foi
            // desfeita, então a linha do outbox também. A repetição não publica
            // um segundo OrderCreated, e o cliente não é cobrado duas vezes —
            // esta cláusula sustenta RN-9 sem escrever nada sobre pagamento.
            return new CreateOrderResult(
                ToResponse(await FindExistingAsync(request.CustomerId, idempotencyKey, cancellationToken)),
                AlreadyExisted: true);
        }

        return new CreateOrderResult(ToResponse(order), AlreadyExisted: false);
    }

    private async Task<Order> FindExistingAsync(
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Sem rastreamento: o pedido recusado continua no ChangeTracker como
        // `Added`, e este contexto não vai salvar de novo. Ler destacado evita
        // que o EF misture o que falhou com o que veio do banco.
        var existing = await dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.CustomerId == customerId
                    && candidate.IdempotencyKey == idempotencyKey,
                cancellationToken);

        // Se o banco recusou por duplicidade, a linha existe e está commitada:
        // o Postgres segura a segunda inserção até a primeira transação
        // terminar, e só então levanta a violação. Chegar aqui sem encontrar
        // nada significaria que alguém apagou o pedido no meio — coisa que esta
        // feature não faz. Falhar alto é melhor que devolver um 200 vazio.
        return existing ?? throw new InvalidOperationException(
            $"Violação de unicidade em {OrderConfiguration.UniqueIdempotencyIndexName}, "
            + "mas o pedido correspondente não foi encontrado.");
    }

    private static CreateOrderResponse ToResponse(Order order) =>
        new(order.Id, order.Status.ToString(), order.TotalAmount);

    // Só a violação *deste* índice significa repetição. Qualquer outra violação
    // de unicidade é defeito e deve continuar subindo como erro: engolir tudo
    // que for 23505 transformaria um bug futuro numa resposta de sucesso.
    private static bool IsDuplicateIdempotencyKey(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OrderConfiguration.UniqueIdempotencyIndexName
        };
}
