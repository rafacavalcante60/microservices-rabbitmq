using PaymentService.Domain;

namespace PaymentService.Infrastructure;

public static class PaymentGatewayExtensions
{
    public static IServiceCollection AddSimulatedPaymentGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SimulatedPaymentGatewayOptions>(
            configuration.GetSection(SimulatedPaymentGatewayOptions.SectionName));

        // Singleton: o simulador não guarda estado por requisição e não abre
        // conexão. Uma integração real com HttpClient entraria aqui como
        // AddHttpClient<IPaymentGateway, ...>, e nenhum chamador notaria.
        services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

        return services;
    }
}
