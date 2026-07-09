using System.Text.Json;

/// <summary>Một đơn hàng đã thất bại vĩnh viễn, kèm ngữ cảnh đủ để điều tra/xử lý lại.</summary>
public sealed record DeadLetterOrder(
    string OrderId,
    string ErrorType,
    string ErrorMessage,
    int Attempts,
    DateTimeOffset FailedAt);

public interface IDeadLetterQueue
{
    Task EnqueueAsync(DeadLetterOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeadLetterOrder>> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dead Letter Queue bền vững tối thiểu: mỗi đơn hỏng là một dòng JSON (định dạng
/// JSON Lines) append vào data/dead-letter-orders.jsonl — sống sót qua restart, đọc
/// lại được qua GET /dead-letters, và dễ thay bằng bảng SQL / queue thật sau này vì
/// worker chỉ phụ thuộc IDeadLetterQueue. SemaphoreSlim tuần tự hóa việc ghi vì có
/// tới MaxDegreeOfParallelism consumer có thể cùng đẩy đơn hỏng một lúc.
/// </summary>
public sealed class FileDeadLetterQueue : IDeadLetterQueue
{
    private const string FilePath = "data/dead-letter-orders.jsonl";

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<FileDeadLetterQueue> _logger;

    public FileDeadLetterQueue(ILogger<FileDeadLetterQueue> logger)
    {
        _logger = logger;
    }

    public async Task EnqueueAsync(DeadLetterOrder order, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await File.AppendAllTextAsync(FilePath, JsonSerializer.Serialize(order) + Environment.NewLine, cancellationToken);
            _logger.LogWarning("Đơn {OrderId} đã được ghi vào Dead Letter Queue ({File}).", order.OrderId, FilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<DeadLetterOrder>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            string[] lines = await File.ReadAllLinesAsync(FilePath, cancellationToken);
            return lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<DeadLetterOrder>(line)!)
                .ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
