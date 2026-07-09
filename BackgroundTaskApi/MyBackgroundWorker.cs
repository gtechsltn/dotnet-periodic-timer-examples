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

        int i = 0;
        int maxIterations = 10; // Giới hạn chạy đến N lần (ví dụ: 10 lần)

        // Giới hạn thời gian chạy (ví dụ: dừng sau 30 giây)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Kết hợp token của hệ thống và token của thời gian chạy
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        // Điều kiện dừng:
        // 1. stoppingToken được kích hoạt (app tắt)
        // 2. timeoutCts được kích hoạt (hết 30 giây)
        // 3. Số lần lặp i đã đạt đến maxIterations
        while (i < maxIterations && !linkedCts.Token.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(linkedCts.Token);

                if (await _semaphore.WaitAsync(0, linkedCts.Token))
                {
                    try
                    {
                        await DoSomethingHeavy();
                        i++; // Chỉ tăng i khi tác vụ hoàn thành
                        _logger.LogInformation("Đã hoàn thành vòng lặp thứ {i}/{max}", i, maxIterations);
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
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Service đã dừng do đạt giới hạn thời gian hoặc yêu cầu hủy.");
                break;
            }
        }

        _logger.LogInformation("Service đã hoàn thành công việc và dừng lại.");
    }

    private async Task DoSomethingHeavy()
    {
        // Giả lập xử lý nặng
        _logger.LogInformation("Bắt đầu tác vụ nặng (mất 3 giây)...");
        await Task.Delay(3000);
        _logger.LogInformation("Tác vụ nặng đã xong.");
    }
}