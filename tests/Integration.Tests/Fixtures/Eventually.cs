using System.Diagnostics;

namespace Integration.Tests.Fixtures;

// Espera ativa com timeout, nunca `Task.Delay` fixo.
//
// O fluxo é eventualmente consistente: entre o 201 do POST e o pedido virar
// `Paid` passam um outbox, duas filas e dois consumidores. Um `Task.Delay(2000)`
// tem que ser generoso o bastante para o pior caso e, ainda assim, falha no dia
// em que a máquina de CI está lenta — e paga o tempo cheio mesmo quando o
// resultado chegou em 200ms. O polling devolve assim que a condição vale e só
// gasta o timeout inteiro quando o sistema realmente não convergiu.
public static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static async Task<T> UntilAsync<T>(
        Func<Task<T?>> probe,
        string description,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var limit = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var result = await probe();

            if (result is not null)
            {
                return result;
            }

            if (stopwatch.Elapsed >= limit)
            {
                throw new TimeoutException(
                    $"Condição não satisfeita em {limit.TotalSeconds:0.#}s: {description}");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public static async Task UntilAsync(
        Func<Task<bool>> condition,
        string description,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // O sentinela existe só para reaproveitar a sobrecarga acima: `bool` não
        // é tipo de referência e não tem como sinalizar "ainda não" com null.
        await UntilAsync<string>(
            async () => await condition() ? "satisfeita" : null,
            description,
            timeout,
            cancellationToken);
    }
}
