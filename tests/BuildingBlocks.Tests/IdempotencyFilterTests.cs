using System.Collections.Concurrent;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Tests;

// Princípio IV. O filtro é testado dentro de um barramento de verdade (o
// transporte em memória do MassTransit), e não chamando `Send` na mão: o que
// interessa é o comportamento no pipeline — descartar sem erro, confirmar a
// mensagem, deixar o consumidor intacto. Um teste que chamasse o método direto
// provaria menos.
public class IdempotencyFilterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact] // Princípio IV — a mesma mensagem duas vezes processa uma vez só
    public async Task Mensagem_repetida_e_processada_uma_unica_vez()
    {
        await using var provider = BuildProvider(new FakeIdempotencyStore(), out var counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = NewId.NextGuid();

        await Publish(harness, messageId);
        await Publish(harness, messageId);

        await WaitUntilStable(counter);
        counter.Count.Should().Be(1);
    }

    [Fact] // MessageIds diferentes são mensagens diferentes, não duplicatas
    public async Task Mensagens_distintas_sao_ambas_processadas()
    {
        await using var provider = BuildProvider(new FakeIdempotencyStore(), out var counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await Publish(harness, NewId.NextGuid());
        await Publish(harness, NewId.NextGuid());

        await WaitUntil(() => counter.Count == 2);
        counter.Count.Should().Be(2);
    }

    [Fact] // R-4 — falha aberta: Redis fora não pode parar o consumidor
    public async Task Store_indisponivel_deixa_a_mensagem_passar()
    {
        await using var provider = BuildProvider(new BrokenIdempotencyStore(), out var counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await Publish(harness, NewId.NextGuid());

        await WaitUntil(() => counter.Count == 1);
        counter.Count.Should().Be(1);
    }

    [Fact] // R-4 — sem a checagem, a duplicata passa: é o preço da falha aberta,
           // e quem segura o dano são as constraints únicas no banco (T-015)
    public async Task Store_indisponivel_deixa_passar_ate_a_duplicata()
    {
        await using var provider = BuildProvider(new BrokenIdempotencyStore(), out var counter);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = NewId.NextGuid();

        await Publish(harness, messageId);
        await Publish(harness, messageId);

        await WaitUntil(() => counter.Count == 2);
        counter.Count.Should().Be(2);
    }

    [Fact] // A chave é por consumidor: dois consumidores veem o mesmo evento
    public async Task Consumidores_diferentes_processam_o_mesmo_evento()
    {
        // Dois consumidores do mesmo evento, cada um na sua fila — é o que
        // acontece de verdade com PaymentApproved, consumido pelo OrderService
        // e pelo NotificationService, com o mesmo MessageId e um Redis só.
        await using var provider = BuildProviderWithTwoConsumers(
            new FakeIdempotencyStore(), out var orders, out var notifications);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = NewId.NextGuid();

        await Publish(harness, messageId);
        await Publish(harness, messageId);

        await WaitUntil(() => orders.Count == 1 && notifications.Count == 1);

        // Se a chave não incluísse o consumidor, o segundo descartaria um
        // evento que ele nunca viu — e a notificação do cliente sumiria.
        orders.Count.Should().Be(1);
        notifications.Count.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(IIdempotencyStore store, out ProcessedCounter counter)
    {
        counter = new ProcessedCounter();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(store)
            .AddSingleton(counter)
            .AddMassTransitTestHarness(configurator =>
            {
                configurator.AddConsumer<CountingConsumer>();

                configurator.UsingInMemory((context, bus) =>
                {
                    bus.UseIdempotency(context);
                    bus.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);
    }

    private static ServiceProvider BuildProviderWithTwoConsumers(
        IIdempotencyStore store, out ProcessedCounter first, out ProcessedCounter second)
    {
        first = new ProcessedCounter();
        second = new ProcessedCounter();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(store)
            .AddSingleton(new CounterPair(first, second))
            .AddMassTransitTestHarness(configurator =>
            {
                configurator.AddConsumer<CountingConsumer>();
                configurator.AddConsumer<OtherCountingConsumer>();

                configurator.UsingInMemory((context, bus) =>
                {
                    bus.UseIdempotency(context);
                    bus.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);
    }

    private static Task Publish(ITestHarness harness, Guid messageId) =>
        harness.Bus.Publish(new SomethingHappened(Guid.NewGuid()),
            context => context.MessageId = messageId);

    // Espera com condição e timeout, nunca `Task.Delay` fixo: delay fixo é
    // lento quando passa e intermitente quando não passa.
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    // Para o caso "não deve processar de novo" não basta esperar a condição:
    // ela já está satisfeita antes de a segunda mensagem sequer chegar. Aqui se
    // espera a primeira e depois se dá tempo à segunda de aparecer — se ela
    // aparecer, o contador denuncia.
    private static async Task WaitUntilStable(ProcessedCounter counter)
    {
        await WaitUntil(() => counter.Count >= 1);
        await Task.Delay(300);
    }

    public record SomethingHappened(Guid Id);

    private class ProcessedCounter
    {
        private readonly ConcurrentBag<Guid> _processed = [];

        public int Count => _processed.Count;

        public void Record(Guid id) => _processed.Add(id);
    }

    private record CounterPair(ProcessedCounter First, ProcessedCounter Second);

    private class CountingConsumer(IServiceProvider services) : IConsumer<SomethingHappened>
    {
        public Task Consume(ConsumeContext<SomethingHappened> context)
        {
            CounterFor(services, pair => pair.First).Record(context.Message.Id);

            return Task.CompletedTask;
        }
    }

    // Segundo consumidor do mesmo evento, em outra fila.
    private class OtherCountingConsumer(IServiceProvider services) : IConsumer<SomethingHappened>
    {
        public Task Consume(ConsumeContext<SomethingHappened> context)
        {
            CounterFor(services, pair => pair.Second).Record(context.Message.Id);

            return Task.CompletedTask;
        }
    }

    private static ProcessedCounter CounterFor(IServiceProvider services, Func<CounterPair, ProcessedCounter> pick)
    {
        var pair = services.GetService<CounterPair>();

        return pair is null ? services.GetRequiredService<ProcessedCounter>() : pick(pair);
    }

    private class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, byte> _seen = new();

        public Task<bool> TryMarkAsProcessedAsync(
            string consumer, Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(_seen.TryAdd(RedisIdempotencyStore.KeyFor(consumer, messageId), 0));
    }

    // O Redis fora do ar.
    private class BrokenIdempotencyStore : IIdempotencyStore
    {
        public Task<bool> TryMarkAsProcessedAsync(
            string consumer, Guid messageId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis indisponível.");
    }
}
