# Using PeriodicTimer with SemaphoreSlim in a Console App
* LongRunningTaskApi: Không bỏ xót đơn hàng nào
    * Microsoft.Extensions.Hosting.BackgroundService
    * System.Threading.Channels & Queue (hàng đợi)
    * Resilience: Sử dụng Polly để thực hiện Retry Policy thông qua thư viện Microsoft.Extensions.Http.Polly
    * Parallelism: Enumerable.Range & Task.WhenAll
    * Graceful Shutdown: IHostApplicationLifetime
    * Serilog
    * Swagger
* LongRunningTaskApi: Xử lý đơn hàng
    * Microsoft.Extensions.Hosting.BackgroundService
    * System.Threading.Channels
    * Serilog
* BackgroundTaskApi              ~ Microsoft.Extensions.Hosting.BackgroundService
* dotnet-periodic-timer-examples ~ System.Threading.PeriodicTimer & SemaphoreSlim
* system-timers-timer-examples   ~ System.Timers.Timer & SemaphoreSlim
* Sample code:
* https://github.com/gtechsltn/dotnet-periodic-timer-examples
* https://dev.to/karenpayneoregon/c-periodictimer-2ed
* https://www.honlsoft.com/blog/2021-10-27-net-6-periodic-timer
* https://docs.google.com/document/d/1F4uYAvtSLr3ujEbJJPO_E3Wh7y0ewraXl0zP_bLzWO0

```
dotnet add package Serilog.AspNetCore
dotnet add package Microsoft.Extensions.Http.Polly
dotnet add package Swashbuckle.AspNetCore --version 6.9.0
```

# dotnet-periodic-timer-examples

Sử dụng `System.Threading.PeriodicTimer` để thực hiện các tác vụ nền định kỳ trong một ứng dụng console.

Giải thích cách hoạt động của đoạn code này:

Tick thứ 1 (0s): WaitAsync(0) thành công, tác vụ DoSomethingHeavy bắt đầu chạy. Nó sẽ chạy trong 3 giây.

Tick thứ 2 (1s) & Tick thứ 3 (2s): Khi timer kích hoạt, semaphore.WaitAsync(0) sẽ thấy chốt đang bị giữ (vì tác vụ 1 chưa xong), nên nó trả về false. Chương trình in ra dòng: "Tác vụ trước vẫn đang chạy, bỏ qua lần này".

Sau 3 giây (3s): Tác vụ 1 hoàn tất, semaphore.Release() được gọi. Chốt mở ra.

Tick thứ 4 (4s): Timer kích hoạt, WaitAsync(0) lại thành công và tác vụ mới lại được bắt đầu.

Cách làm này đảm bảo hệ thống của bạn luôn ổn định, không bao giờ bị "ngập lụt" bởi các tác vụ tồn đọng và không bao giờ xảy ra lỗi xung đột tài nguyên giữa các lần chạy.

**Program.cs**:

```
using System.Text;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("Ứng dụng bắt đầu. Nhấn Ctrl+C để dừng.");

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\nĐang dừng ứng dụng...");
            cts.Cancel();
            e.Cancel = true;
        };

        try
        {
            await RunTimerAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Ứng dụng đã dừng thành công.");
        }
    }

    private static async Task RunTimerAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer kích hoạt mỗi 1 giây
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        // Semaphore chỉ cho phép 1 tác vụ chạy tại một thời điểm
        using SemaphoreSlim semaphore = new(1, 1);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Kiểm tra xem có đang chạy tác vụ nào không.
            // Nếu có, WaitAsync(0) sẽ trả về false ngay lập tức.
            if (await semaphore.WaitAsync(0, stoppingToken))
            {
                try
                {
                    await DoSomethingHeavy();
                }
                finally
                {
                    // Giải phóng chốt để lần tick tiếp theo có thể chạy
                    semaphore.Release();
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ trước vẫn đang chạy, bỏ qua lần này.");
            }
        }
    }

    private static async Task DoSomethingHeavy()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Bắt đầu tác vụ nặng (mất 3 giây)...");

        // Giả lập tác vụ mất 3 giây
        await Task.Delay(3000);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ nặng đã xong.");
    }
}
```

# system-timers-timer-examples

Sử dụng `System.Timers.Timer` để thực hiện các tác vụ nền định kỳ trong một ứng dụng console.

**Program.cs**:

```
using System.Text;
using System.Timers;

internal class Program
{
    // Cần khai báo static để tránh bị Garbage Collector thu hồi
    private static System.Timers.Timer _timer = default!;

    private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private static void Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("Ứng dụng bắt đầu. Nhấn phím bất kỳ để dừng.");

        // 1. Khởi tạo Timer với khoảng thời gian 1 giây
        _timer = new System.Timers.Timer(1000);

        // 2. Hook vào sự kiện Elapsed
        _timer.Elapsed += OnTimerElapsed!;

        // 3. Cho phép tự lặp lại
        _timer.AutoReset = true;

        // 4. Bật timer
        _timer.Enabled = true;

        Console.ReadKey();

        // Dọn dẹp tài nguyên
        _timer.Dispose();
        _semaphore.Dispose();
    }

    private static async void OnTimerElapsed(object sender, ElapsedEventArgs e)
    {
        // Kiểm tra Semaphore để tránh chồng chéo
        if (await _semaphore.WaitAsync(0))
        {
            try
            {
                await DoSomethingHeavy();
            }
            finally
            {
                _semaphore.Release();
            }
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ trước vẫn đang chạy, bỏ qua lần này.");
        }
    }

    private static async Task DoSomethingHeavy()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Bắt đầu tác vụ nặng (mất 3 giây)...");
        await Task.Delay(3000); // Giả lập công việc
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Tác vụ nặng đã xong.");
    }
}
```

# BackgroundTaskApi

Sử dụng `Microsoft.Extensions.Hosting.BackgroundService` để thực hiện các tác vụ nền định kỳ trong một ứng dụng ASP.NET Core Minimal API.

**Program.cs**:

```
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Serilog để log ra tệp (logs/myapp.txt)
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
```

**MyBackgroundWorker.cs**:

```
public class MyBackgroundWorker : BackgroundService
{
    private readonly ILogger<MyBackgroundWorker> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public MyBackgroundWorker(ILogger<MyBackgroundWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background Service bắt đầu.");

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Kiểm tra semaphore để tránh chồng chéo
            if (await _semaphore.WaitAsync(0, stoppingToken))
            {
                try
                {
                    await DoSomethingHeavy();
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            else
            {
                _logger.LogWarning("Tác vụ trước vẫn đang chạy, bỏ qua lần này.");
            }
        }
    }

    private async Task DoSomethingHeavy()
    {
        _logger.LogInformation("Bắt đầu tác vụ nặng (mất 3 giây)...");
        await Task.Delay(3000);
        _logger.LogInformation("Tác vụ nặng đã xong.");
    }
}
```

# LongRunningTaskApi

**Program.cs**:

```
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
```

**OrderProcessorWorker.cs**:

Kế thừa từ `BackgroundService`

```
public class OrderProcessorWorker : BackgroundService
{
    private readonly OrderChannel _orderChannel;
    private readonly ILogger<OrderProcessorWorker> _logger;

    public OrderProcessorWorker(OrderChannel orderChannel, ILogger<OrderProcessorWorker> logger)
    {
        _orderChannel = orderChannel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker xử lý đơn hàng đã khởi động.");

        // Đọc liên tục khi có dữ liệu mới
        await foreach (var orderId in _orderChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Đang xử lý đơn hàng: {OrderId}", orderId);

                // Giả lập xử lý đơn hàng nặng (ví dụ: gọi API bên thứ 3, xử lý logic phức tạp)
                await DoSomethingHeavy(stoppingToken);

                _logger.LogInformation("Đã xử lý xong đơn hàng: {OrderId}", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý đơn hàng {OrderId}", orderId);
            }
        }
    }

    private async Task DoSomethingHeavy(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bắt đầu tác vụ nặng (mất 3 giây)...");
        await Task.Delay(3000, stoppingToken);
        _logger.LogInformation("Tác vụ nặng đã xong.");
    }
}
```

**OrderChannel.cs:**

```
using System.Threading.Channels;

public class OrderChannel
{
    // Channel với dung lượng giới hạn (Bounded) để tránh tràn bộ nhớ nếu đơn hàng quá nhiều
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait // Nếu channel đầy, Producer sẽ đợi cho đến khi có chỗ trống
    });

    public ChannelWriter<string> Writer => _channel.Writer;
    public ChannelReader<string> Reader => _channel.Reader;
}
```

# Unicode (UTF-8): Bạn nên đặt encoding để hiển thị tiếng Việt đúng cách:
```
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
```

# Tham khảo
* https://docs.google.com/document/d/1F4uYAvtSLr3ujEbJJPO_E3Wh7y0ewraXl0zP_bLzWO0
* https://docs.google.com/document/d/18SFwdKIdasjQKUoMDQhZyzQhL5Q3kFGrZ8T3CCQ-dfc
* https://docs.google.com/document/d/1aSbPBs-VhXiHdF6nmchTGZn-c5HQQqSIouia4ZDapM0