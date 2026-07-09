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

// 2. Đăng ký Hosted Service của bạn
builder.Services.AddHostedService<MyBackgroundWorker>();

var app = builder.Build();

app.MapGet("/", () => "API đang chạy! Kiểm tra tệp log trong thư mục /logs");

app.Run();