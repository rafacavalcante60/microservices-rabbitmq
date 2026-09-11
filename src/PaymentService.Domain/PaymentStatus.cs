namespace PaymentService.Domain;

// Três valores e nenhum `Pending`: um Payment só existe depois que a cobrança
// terminou. Enquanto o gateway não respondeu não há pagamento nenhum para
// registrar — há uma tentativa em andamento, que vive na memória do consumidor,
// não numa linha do banco.
public enum PaymentStatus
{
    Approved,
    Declined,
    Failed
}
