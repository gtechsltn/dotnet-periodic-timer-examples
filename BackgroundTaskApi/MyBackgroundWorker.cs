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