using System.Globalization;

namespace PaymentService.Domain;

// RN-10. A regra do processador simulado vive no domínio, e isso merece uma
// ressalva honesta: num sistema real a decisão viria na resposta do gateway, e
// o domínio apenas registraria o que ele respondeu. Aqui o processador é
// simulado por decisão de escopo da spec, e a spec o descreve como regra de
// negócio — então é aqui que ela é testável sem subir nada.
//
// O que **não** muda com a simulação: a decisão é determinística. O mesmo valor
// produz sempre o mesmo resultado, senão a demonstração ao vivo dependeria de
// sorte e os testes seriam intermitentes.
public static class PaymentApprovalRule
{
    // 10_000.00m e não 10000: o separador `_` torna a ordem de grandeza legível
    // de relance, e o sufixo `m` é decimal. Um `double` aqui erraria centavos.
    public const decimal ApprovalLimit = 10_000.00m;

    // `<=` e não `<`: o limite é inclusivo (CA-8). Exatamente R$ 10.000,00
    // aprova; R$ 10.000,01 recusa. É o tipo de fronteira que passa despercebida
    // na leitura e aparece como bug em produção, por isso tem teste nos dois
    // lados e no centavo do meio.
    public static bool Approves(decimal amount) => amount <= ApprovalLimit;

    // Cultura explícita, e não a ambiente: `$"{amount:N2}"` formataria
    // conforme a máquina que roda o processo — "10.000,01" no meu WSL e
    // "10,000.01" num contêiner com locale padrão. O motivo da recusa vai
    // parar no banco, no evento e na notificação do cliente, e moeda que troca
    // de separador conforme o servidor que atendeu é defeito, não detalhe.
    private static readonly CultureInfo Brazilian = CultureInfo.GetCultureInfo("pt-BR");

    public static string DeclineReasonFor(decimal amount) =>
        string.Format(
            Brazilian,
            "Valor de R$ {0:N2} acima do limite de R$ {1:N2} do processador.",
            amount,
            ApprovalLimit);
}
