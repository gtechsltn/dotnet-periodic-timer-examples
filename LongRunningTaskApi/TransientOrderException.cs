/// <summary>
/// Lỗi TẠM THỜI (transient) khi xử lý đơn — mạng chập chờn, service phía sau quá tải,
/// deadlock/timeout DB... — tức là loại lỗi mà thử lại có cơ hội thành công.
/// Retry policy trong OrderProcessorWorker CHỈ retry loại lỗi này (cùng
/// TimeoutException/HttpRequestException); lỗi khác (bug, dữ liệu sai) được coi là
/// vĩnh viễn và đi thẳng vào Dead Letter Queue — retry một NullReferenceException
/// thêm 3 lần chỉ tốn thời gian chứ không bao giờ tự lành.
/// </summary>
public sealed class TransientOrderException : Exception
{
    public TransientOrderException(string message) : base(message)
    {
    }

    public TransientOrderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
