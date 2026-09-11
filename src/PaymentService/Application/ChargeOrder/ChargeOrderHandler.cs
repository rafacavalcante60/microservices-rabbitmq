using Microsoft.Extensions.Options;
using PaymentService.Domain;
using Polly;
using Polly.Retry;

namespace PaymentService.Application.ChargeOrder;

// D-6 / RN-11. Este é o ponto onde o projeto tem **dois** retries diferentes, e
// saber por que eles não são o mesmo é pergunta boa de entrevista:
//
// - Aqui (Polly): o gateway não respondeu. Isso é caminho de negócio previsto
//   pela spec — depois de esgotar as tentativas, o pedido termina em
//   `PaymentFailed`, com notificação ao cliente (CA-13). O consumidor da T-017
//   conclui normalmente e dá `ack` na mensagem.
// - Lá (MassTransit, D-7): o *nosso* banco caiu. Isso é defeito de
//   infraestrutura, não desfecho de pagamento. A mensagem retenta e, se
//   persistir, vai para a DLQ — sem inventar um desfecho que não aconteceu.
//
// Juntar os dois num retry só mascararia um Postgres fora do ar como "pagamento
// falhou", e o cliente receberia uma notificação de falha por um problema que
// não é dele.
public class ChargeOrderHandler
{
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<ChargeOrderHandler> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly int _maxAttempts;

    public ChargeOrderHandler(
        IPaymentGateway gateway,
        IOptions<PaymentRetryOptions> options,
        ILogger<ChargeOrderHandler> logger)
    {
        _gateway = gateway;
        _logger = logger;
        _maxAttempts = options.Value.MaxAttempts;

        if (_maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), _maxAttempts, "A cobrança precisa de ao menos uma tentativa.");
        }

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Só indisponibilidade é retentada. Recusa nem chega aqui: ela
                // volta como resultado, não como exceção (T-013), e repetir uma
                // recusa seria insistir com quem já decidiu.
                ShouldHandle = new PredicateBuilder().Handle<PaymentGatewayUnavailableException>(),

                // 5 tentativas = 1 chamada + 4 retentativas. O off-by-one aqui
                // é silencioso: com MaxRetryAttempts = 5 seriam 6 idas ao
                // gateway, e o teste que conta chamadas é o que prende isso.
                MaxRetryAttempts = _maxAttempts - 1,

                Delay = options.Value.BaseDelay,

                // Backoff exponencial: se o gateway caiu por sobrecarga,
                // repetir em intervalo fixo mantém exatamente a pressão que o
                // derrubou. Dobrar a espera dá tempo de ele se recuperar.
                BackoffType = DelayBackoffType.Exponential,

                // Jitter: sem ele, mil consumidores que falharam juntos
                // retentam juntos, em ondas sincronizadas. O ruído aleatório
                // espalha as retentativas no tempo.
                UseJitter = true,

                OnRetry = arguments =>
                {
                    _logger.LogWarning(
                        "Tentativa {Attempt} de cobrança falhou; repetindo em {Delay}.",
                        arguments.AttemptNumber + 1,
                        arguments.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    // Devolve o agregado com o desfecho, sem persistir nada: gravar em
    // `payments` e publicar o evento é a T-015 / T-017. Aqui é só "cobrar e
    // dizer o que aconteceu".
    public async Task<Payment> HandleAsync(PaymentChargeRequest request, CancellationToken cancellationToken)
    {
        // Contado do lado de fora da política porque é o número que vai para a
        // coluna `attempts` e para o log de suporte: saber que o desfecho veio
        // de primeira ou na quinta é o que diferencia "gateway instável" de
        // "gateway fora do ar".
        var attempts = 0;

        try
        {
            var result = await _pipeline.ExecuteAsync(
                async token =>
                {
                    attempts++;
                    return await _gateway.ChargeAsync(request, token);
                },
                cancellationToken);

            return result.Approved
                ? Payment.Approved(request.OrderId, request.CustomerId, request.Amount, attempts)
                : Payment.Declined(
                    request.OrderId, request.CustomerId, request.Amount, result.DeclineReason!, attempts);
        }
        catch (PaymentGatewayUnavailableException exception)
        {
            // Tentativas esgotadas. Note que a exceção **não** é propagada: se
            // subisse, o consumidor da T-017 falharia e a mensagem iria para a
            // DLQ sem publicar desfecho nenhum — e a spec exige que o cliente
            // seja notificado da falha (CA-13). Falha do gateway é resposta,
            // não acidente.
            _logger.LogError(
                exception,
                "Cobrança do pedido {OrderId} falhou após {Attempts} tentativas.",
                request.OrderId,
                attempts);

            return Payment.Failed(
                request.OrderId,
                request.CustomerId,
                request.Amount,
                $"Processador de pagamento não respondeu após {attempts} tentativas.",
                attempts);
        }
    }
}
