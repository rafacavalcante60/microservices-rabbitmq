using PaymentService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSimulatedPaymentGateway(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
