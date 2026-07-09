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