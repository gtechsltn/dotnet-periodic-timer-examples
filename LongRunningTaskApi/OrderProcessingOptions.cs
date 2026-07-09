/// <summary>
/// Toàn bộ "con số ma thuật" của pipeline xử lý đơn hàng, bind từ section
/// "OrderProcessing" trong appsettings.json — thay vì hard-code rải rác trong
/// OrderChannel / OrderProcessorWorker. Đổi cấu hình không cần build lại.
/// </summary>
public sealed class OrderProcessingOptions
{
    public const string SectionName = "OrderProcessing";

    /// <summary>Dung lượng tối đa của Channel. Đầy thì producer (endpoint POST) phải đợi.</summary>
    public int ChannelCapacity { get; set; } = 100;

    /// <summary>Số consumer đọc Channel song song.</summary>
    public int MaxDegreeOfParallelism { get; set; } = 5;

    /// <summary>Số lần retry cho lỗi transient (không tính lần chạy đầu).</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Delay cơ sở (giây) cho exponential backoff: lần thử n đợi
    /// RetryBaseDelaySeconds * 2^(n-1) giây (1s, 2s, 4s... với giá trị mặc định).
    /// </summary>
    public double RetryBaseDelaySeconds { get; set; } = 1;

    /// <summary>Thời gian giả lập của tác vụ nặng (ms).</summary>
    public int HeavyTaskDurationMs { get; set; } = 3000;

    /// <summary>
    /// Xác suất (0..1) tác vụ giả lập ném lỗi transient — để demo được đường retry
    /// và Dead Letter Queue. Đặt 0 để tắt.
    /// </summary>
    public double SimulatedTransientFailureRate { get; set; } = 0.3;
}
