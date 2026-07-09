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

// --- CẤU HÌNH SWAGGER ---
builder.Services.AddEndpointsApiExplorer(); // Cần thiết để khám phá các endpoint
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Order Processing API",
        Version = "v1"
    });
});
// ------------------------

// Đăng ký Channel là Singleton
builder.Services.AddSingleton<OrderChannel>();

// Đăng ký Background Service
builder.Services.AddHostedService<OrderProcessorWorker>();

var app = builder.Build();

// --- KÍCH HOẠT SWAGGER ---
// Chỉ hiển thị Swagger khi ứng dụng chạy ở môi trường Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = string.Empty; // Để Swagger hiện ngay tại trang chủ (http://localhost:port/)
    });
}
// -------------------------

// API Producer: Đẩy đơn hàng vào hàng đợi
app.MapPost("/order/{orderId}", async (string orderId, OrderChannel orderChannel) =>
{
    await orderChannel.Writer.WriteAsync(orderId);
    return Results.Accepted();
});

// Lắng nghe sự kiện ứng dụng tắt để đóng Channel
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    // Đóng channel để worker biết dừng đọc khi đã xử lý hết đơn tồn đọng
    app.Services.GetRequiredService<OrderChannel>().Writer.TryComplete();
});

app.Run();