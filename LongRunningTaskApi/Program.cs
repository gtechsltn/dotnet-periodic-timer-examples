using Serilog;

using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Serilog để log ra tệp (logs/LongRunningTaskApi.txt)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/LongRunningTaskApi.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Đăng ký Channel là Singleton
builder.Services.AddSingleton<OrderChannel>();

// Đăng ký Background Service
builder.Services.AddHostedService<OrderProcessorWorker>();

var app = builder.Build();

// API Producer: Đẩy đơn hàng vào hàng đợi
app.MapPost("/order/{orderId}", async (string orderId, OrderChannel orderChannel) =>
{
    // Đẩy đơn hàng vào channel
    await orderChannel.Writer.WriteAsync(orderId);
    return Results.Accepted($"/order/status/{orderId}", "Đơn hàng đã được nhận vào hàng đợi.");
});

app.Run();