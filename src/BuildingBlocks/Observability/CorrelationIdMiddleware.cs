using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace BuildingBlocks.Observability;

// Princípio VII. Um pedido atravessa três serviços por mensagens assíncronas:
// quando algo dá errado, não existe stack trace que ligue o 500 do Notifications
// ao POST que o originou dois saltos atrás. O CorrelationId é esse fio.
//
// Ele **lê** o header antes de gerar um novo, e essa ordem é o ponto todo: o
// identificador nasce uma vez só, na borda (o gateway, na T-021), e todo mundo
// depois apenas repassa. Um serviço que gerasse o seu próprio quebraria a
// corrente exatamente onde ela precisa continuar.
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context);

        // Devolvido no response para que quem chamou consiga citar o
        // identificador ao abrir um chamado — inclusive quando ele foi gerado
        // aqui e o cliente ainda não o conhecia.
        context.Response.Headers[HeaderName] = correlationId;

        // Escrito de volta no **request**, e não só no response: o YARP
        // encaminha os headers da requisição que recebeu, então um
        // CorrelationId gerado aqui e guardado só no response morreria no
        // gateway. Esta linha é o que faz o identificador nascido na borda
        // chegar ao OrderService — e de lá, pelo evento, aos outros dois.
        context.Request.Headers[HeaderName] = correlationId;

        // Guardado no HttpContext para que o resto da requisição o alcance sem
        // precisar reler o header.
        context.Items[HeaderName] = correlationId;

        // PushProperty enriquece **todo** log escrito dentro deste escopo, sem
        // que nenhum deles precise mencionar o CorrelationId. O `using` é o que
        // fecha o escopo ao fim da requisição; sem ele, o valor vazaria para a
        // próxima requisição atendida pela mesma thread.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string Resolve(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();

        return string.IsNullOrWhiteSpace(incoming) ? Guid.NewGuid().ToString() : incoming;
    }
}

public static class CorrelationIdExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    // Para quem precisa do identificador no meio da requisição — por exemplo o
    // handler que carimba o CorrelationId no evento antes de publicá-lo.
    public static Guid GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value)
        && Guid.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : Guid.Empty;
}
