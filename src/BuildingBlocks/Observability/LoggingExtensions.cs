using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace BuildingBlocks.Observability;

public static class LoggingExtensions
{
    // D-13. Log em texto é legível por uma pessoa lendo um terminal; log em
    // JSON é consultável por máquina. Num fluxo de três serviços, a pergunta
    // que importa é "me mostre tudo que aconteceu com o CorrelationId X", e
    // essa pergunta só existe se cada linha for um objeto com campos — não uma
    // frase onde o identificador está no meio do texto.
    //
    // CompactJsonFormatter e não o JsonFormatter padrão: mesmo conteúdo, nomes
    // de campo curtos (`@t`, `@mt`), arquivo sensivelmente menor. É o formato
    // que Seq, Elastic e Loki já entendem sem configuração.
    public static IHostBuilder UseJsonLogging(this IHostBuilder host) =>
        host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)

            // Sem este enricher o PushProperty do CorrelationIdMiddleware não
            // chega a lugar nenhum: é ele que copia as propriedades do escopo
            // ambiente para dentro de cada evento de log.
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", context.HostingEnvironment.ApplicationName)
            .WriteTo.Console(new CompactJsonFormatter()));
}
