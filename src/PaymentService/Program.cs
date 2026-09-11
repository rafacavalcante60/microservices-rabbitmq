using PaymentService.Application.ChargeOrder;
using PaymentService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSimulatedPaymentGateway(builder.Configuration);

builder.Services.Configure<PaymentRetryOptions>(
    builder.Configuration.GetSection(PaymentRetryOptions.SectionName));
builder.Services.AddScoped<ChargeOrderHandler>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
