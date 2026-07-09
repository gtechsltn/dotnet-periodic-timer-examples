using Microsoft.Extensions.Options;

using Polly;
using Polly.Retry;

public class OrderProcessorWorker : BackgroundService
{
    private readonly OrderChannel _orderChannel;
    private readonly IDeadLetterQueue _deadLetterQueue;
    private readonly ILogger<OrderProcessorWorker> _logger;
    private readonly OrderProcessingOptions _options;
    private readonly AsyncRetryPolicy _retryPolicy;

    public OrderProcessorWorker(
        OrderChannel orderChannel,
        IDeadLetterQueue deadLetterQueue,
        IOptions<OrderProcessingOptions> options,
        ILogger<OrderProcessorWorker> logger)
    {
        _orderChannel = orderChannel;
        _deadLetterQueue = deadLetterQueue;
        _logger = logger;
        _options = options.Value;

        // Polly: CHỈ retry lỗi transient (TransientOrderException / TimeoutException /
        // HttpRequestException) — lỗi vĩnh viễn (bug, dữ liệu sai) đi thẳng vào DLQ,
        // retry thêm chỉ tốn thời gian. Delay theo exponential backoff:
        // base * 2^(n-1) giây (mặc định 1s, 2s, 4s) thay vì cố định 2s mỗi lần.
        _retryPolicy = Policy
            .Handle<Exception>(IsTransient)
            .WaitAndRetryAsync(
                _options.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds * Math.Pow(2, retryAttempt - 1)),
                (ex, delay, retryAttempt, _) => _logger.LogWarning(
                    "Lỗi transient, thử lại lần {Attempt}/{Max} sau {Delay}s. Lỗi: {Message}",
                    retryAttempt, _options.RetryCount, delay.TotalSeconds, ex.Message));
    }

    /// <summary>
    /// Lỗi có đáng thử lại không? OperationCanceledException bị loại tường minh —
    /// hủy (do app dừng) không phải là lỗi xử lý.
    /// </summary>
    private static bool IsTransient(Exception ex) =>
        ex is not OperationCanceledException &&
        ex is TransientOrderException or TimeoutException or HttpRequestException;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker bắt đầu với {Parallelism} luồng xử lý.", _options.MaxDegreeOfParallelism);

        // Tạo danh sách các task xử lý song song
        var workers = Enumerable.Range(0, _options.MaxDegreeOfParallelism)
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
                await _retryPolicy.ExecuteAsync(async () => await DoSomethingHeavy(orderId, stoppingToken));

                _logger.LogInformation("Đã xong đơn: {OrderId}", orderId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App đang dừng giữa chừng một đơn — đây KHÔNG phải đơn hỏng, đừng ghi
                // vào DLQ. (Đơn này bị mất vì Channel in-memory; queue bền sẽ tự trả
                // nó về hàng đợi nhờ cơ chế ack.)
                _logger.LogWarning("Đơn {OrderId} bị hủy giữa chừng do ứng dụng dừng.", orderId);
                break;
            }
            catch (Exception ex)
            {
                // Thất bại vĩnh viễn: hoặc lỗi transient đã hết lượt retry, hoặc lỗi
                // không-transient (không được retry). Ghi vào Dead Letter Queue thật
                // (data/dead-letter-orders.jsonl, xem GET /dead-letters) thay vì chỉ log.
                _logger.LogError(ex, "Đơn hàng {OrderId} thất bại vĩnh viễn, chuyển vào Dead Letter Queue.", orderId);

                // Số lần đã chạy: lỗi transient đi hết RetryCount + 1 lần; lỗi khác chết ngay lần đầu.
                int attempts = IsTransient(ex) ? _options.RetryCount + 1 : 1;

                // CancellationToken.None có chủ đích: bản ghi DLQ phải được ghi xong
                // kể cả khi app đang trong quá trình dừng.
                await _deadLetterQueue.EnqueueAsync(
                    new DeadLetterOrder(orderId, ex.GetType().Name, ex.Message, attempts, DateTimeOffset.UtcNow),
                    CancellationToken.None);
            }
        }
    }

    private async Task DoSomethingHeavy(string orderId, CancellationToken stoppingToken)
    {
        // Giả lập xử lý nặng (thời lượng lấy từ cấu hình)
        _logger.LogInformation("Bắt đầu tác vụ nặng (mất {Duration}ms)...", _options.HeavyTaskDurationMs);
        await Task.Delay(_options.HeavyTaskDurationMs, stoppingToken);

        // Giả lập lỗi transient theo xác suất cấu hình, để demo được retry + DLQ.
        // Đặt OrderProcessing:SimulatedTransientFailureRate = 0 để tắt.
        if (Random.Shared.NextDouble() < _options.SimulatedTransientFailureRate)
        {
            throw new TransientOrderException($"Lỗi tạm thời giả lập khi xử lý đơn {orderId}.");
        }

        _logger.LogInformation("Tác vụ nặng đã xong.");
    }
}
