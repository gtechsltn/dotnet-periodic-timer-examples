using Polly;
using Polly.Retry;

public class OrderProcessorWorker : BackgroundService
{
    private readonly OrderChannel _orderChannel;
    private readonly ILogger<OrderProcessorWorker> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private const int MaxDegreeOfParallelism = 5; // Số worker xử lý song song

    public OrderProcessorWorker(OrderChannel orderChannel, ILogger<OrderProcessorWorker> logger)
    {
        _orderChannel = orderChannel;
        _logger = logger;

        // Cấu hình Polly: Thử lại 3 lần nếu có lỗi, mỗi lần cách nhau 2 giây
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(2),
                (ex, time) => _logger.LogWarning("Lỗi xử lý, thử lại sau {Time}s. Lỗi: {Message}", time.TotalSeconds, ex.Message));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker bắt đầu với {Parallelism} luồng xử lý.", MaxDegreeOfParallelism);

        // Tạo danh sách các task xử lý song song
        var workers = Enumerable.Range(0, MaxDegreeOfParallelism)
            .Select(_ => RunConsumerAsync(stoppingToken));

        // Đợi tất cả các consumer kết thúc khi channel đóng hoặc dừng ứng dụng
        await Task.WhenAll(workers);

        _logger.LogInformation("Tất cả worker đã dừng an toàn.");
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        // Khi dừng app, ReadAllAsync sẽ tự kết thúc nếu channel được đóng (Complete)
        await foreach (var orderId in _orderChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Đang xử lý đơn: {OrderId} trên luồng {ThreadId}", orderId, Environment.CurrentManagedThreadId);

                // Áp dụng chính sách retry của Polly
                await _retryPolicy.ExecuteAsync(async () => await DoSomethingHeavy(stoppingToken));

                _logger.LogInformation("Đã xong đơn: {OrderId}", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Đơn hàng {OrderId} thất bại sau khi đã thử lại.", orderId);
                // Tại đây bạn có thể đẩy đơn vào Dead Letter Queue (DB)
            }
        }
    }

    private async Task DoSomethingHeavy(CancellationToken stoppingToken)
    {
        // Giả lập xử lý nặng
        _logger.LogInformation("Bắt đầu tác vụ nặng (mất 3 giây)...");
        await Task.Delay(3000, stoppingToken);
        _logger.LogInformation("Tác vụ nặng đã xong.");
    }
}