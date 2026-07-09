using Serilog;

using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Serilog để log ra tệp (logs/BackgroundTaskApi.txt)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/BackgroundTaskApi.txt", rollingInterval: RollingInterval.Day)
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

// 2. Đăng ký Hosted Service của bạn
builder.Services.AddHostedService<MyBackgroundWorker>();

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

app.MapGet("/", () => "API đang chạy! Kiểm tra tệp log trong thư mục /logs");

app.Run();