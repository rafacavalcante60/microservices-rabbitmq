using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Health;

public static class HealthCheckExtensions
{
    // A tag que separa "estou vivo" de "consigo trabalhar". Um check registrado
    // com ela entra no /health/ready; sem ela, em nenhum dos dois.
    public const string ReadyTag = "ready";

    // Princípio VI. Os dois endpoints respondem perguntas diferentes, e
    // confundi-los causa estrago real em orquestrador:
    //
    // /health       — o processo está de pé? Não toca em dependência nenhuma.
    //                 Se isto falhar, reiniciar resolve.
    // /health/ready — as dependências respondem? Se o Postgres caiu, reiniciar
    //                 este contêiner não resolve nada — o que se quer é parar
    //                 de mandar tráfego para ele até voltar.
    //
    // Um /health que checasse o banco faria o orquestrador matar e recriar o
    // serviço em loop durante uma queda de Postgres, trocando uma falha parcial
    // por uma indisponibilidade total.
    public static IEndpointRouteBuilder MapDefaultHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync
        });

        return app;
    }

    // O corpo padrão do ASP.NET é a string "Healthy", que não diz *qual*
    // dependência caiu. Numa queda, a primeira pergunta é justamente essa.
    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,

                // A mensagem de exceção entra porque este endpoint é de
                // diagnóstico interno, não é API pública — atrás do gateway,
                // que não roteia /health (D-12).
                error = entry.Value.Exception?.Message
            })
        }));
    }
}
