# Architecture — LongRunningTaskApi

Minimal API (ASP.NET Core, net10.0) minh họa pipeline xử lý đơn hàng **producer/consumer không bỏ sót message**: API nhận đơn → đẩy vào `Channel` có giới hạn → nhiều consumer song song xử lý với retry (Polly) → graceful shutdown xử lý nốt đơn tồn đọng.

## Thành phần

```
Program.cs                  Host: Serilog, Swagger, DI, endpoint POST /order/{orderId}
                            + GET /dead-letters, hook ApplicationStopping để đóng Channel
OrderChannel.cs             Bọc Channel<string> bounded, expose Writer/Reader
OrderProcessorWorker.cs     BackgroundService: N consumer song song + Polly retry + DLQ
OrderProcessingOptions.cs   Options bind từ section "OrderProcessing" (capacity, parallelism,
                            retry, backoff, failure-rate giả lập)
TransientOrderException.cs  Đánh dấu lỗi tạm thời — loại lỗi duy nhất đáng retry
DeadLetterQueue.cs          IDeadLetterQueue + FileDeadLetterQueue (JSON Lines, data/)
appsettings.json            Logging + section OrderProcessing
```

Gói phụ thuộc: `Serilog.AspNetCore` (console + `logs/LongRunningTaskApi.txt`, rolling theo ngày), `Swashbuckle.AspNetCore` (Swagger tại route gốc, Development), `Microsoft.Extensions.Http.Polly` (kéo Polly vào cho retry policy).

## Luồng dữ liệu

```
POST /order/{id}                          ApplicationStopping
      │                                          │
      ▼                                          ▼
OrderChannel.Writer.WriteAsync()          Writer.TryComplete()
      │                                          │
      ▼                                          ▼
Channel<string> bounded(capacity, FullMode=Wait)  ── đóng khi app dừng
      │
      ▼  Reader.ReadAllAsync(stoppingToken)
OrderProcessorWorker: N consumer (Enumerable.Range + Task.WhenAll)
      │  mỗi đơn: retry CHỈ lỗi transient, exponential backoff (1s, 2s, 4s...)
      ▼
  thành công        → log "Đã xong đơn"
  hủy do app dừng   → log Warning, KHÔNG vào DLQ
  thất bại vĩnh viễn → FileDeadLetterQueue (data/dead-letter-orders.jsonl)
                       └── xem lại qua GET /dead-letters
```

## Các quyết định thiết kế

**Mọi tham số vận hành nằm trong cấu hình.** `OrderProcessingOptions` bind từ section `OrderProcessing` trong `appsettings.json` (`Configure<T>` + `IOptions<T>`): dung lượng channel, số consumer, số lần retry, delay cơ sở của backoff, thời lượng tác vụ giả lập và tỉ lệ lỗi giả lập — không còn hằng số hard-code trong `OrderChannel`/`OrderProcessorWorker`; đổi hành vi không cần build lại.

**Backpressure bằng bounded Channel.** `Channel.CreateBounded(capacity, FullMode = Wait)`: khi hàng đợi đầy, producer (`WriteAsync` trong endpoint) *đợi* thay vì làm tràn bộ nhớ hoặc drop đơn. Endpoint trả `202 Accepted` — nhận đơn và xử lý được tách rời hoàn toàn.

**`OrderChannel` là singleton thay vì `Channel<T>` trực tiếp.** Lớp bọc cho DI một kiểu có tên rõ ràng, và chỗ duy nhất quyết định dung lượng/FullMode.

**Song song có kiểm soát.** `MaxDegreeOfParallelism` consumer cùng đọc một `ChannelReader` — Channel tự phân phối an toàn giữa các reader, không cần lock. Mỗi consumer là một vòng `await foreach (Reader.ReadAllAsync(...))`; `Task.WhenAll` giữ `ExecuteAsync` sống đến khi tất cả dừng.

**Retry có chọn lọc + exponential backoff.** Policy chỉ `Handle` các lỗi *transient* (`TransientOrderException`, `TimeoutException`, `HttpRequestException`; `OperationCanceledException` bị loại tường minh — hủy không phải lỗi): lỗi vĩnh viễn (bug, dữ liệu sai) không được retry vì thử lại không bao giờ tự lành. Delay tăng theo cấp số nhân `base × 2^(n-1)` (mặc định 1s, 2s, 4s) thay vì cố định — giảm áp lực lên downstream đang quá tải. `DoSomethingHeavy` ném lỗi transient giả lập theo xác suất cấu hình (`SimulatedTransientFailureRate`) để đường retry/DLQ demo được thật; đặt `0` để tắt.

**Dead Letter Queue thật, không chỉ log.** Đơn thất bại vĩnh viễn (hết lượt retry, hoặc lỗi không-transient) được ghi vào `FileDeadLetterQueue` — mỗi đơn một dòng JSON trong `data/dead-letter-orders.jsonl` (sống sót qua restart), kèm loại lỗi, message, số lần đã thử và thời điểm; tra cứu qua `GET /dead-letters`. Worker chỉ phụ thuộc `IDeadLetterQueue` nên thay bằng bảng SQL/queue thật chỉ đụng một class. Hai chi tiết có chủ đích: ghi DLQ dùng `CancellationToken.None` để bản ghi không bị mất khi app đang dừng, và `OperationCanceledException` lúc shutdown **không** vào DLQ (đơn bị hủy giữa chừng không phải đơn hỏng). Một đơn hỏng vẫn không giết worker — exception được catch trong vòng consumer.

**Graceful shutdown không bỏ sót đơn.** `ApplicationStopping.Register(() => Writer.TryComplete())`: đóng phía ghi khi app tắt. `ReadAllAsync` sau đó **vẫn trả nốt các đơn còn trong hàng đợi** rồi mới kết thúc — consumer xử lý sạch backlog trước khi dừng. (`stoppingToken` vẫn là đường thoát cứng nếu host hết kiên nhẫn.)

## So với các project anh em trong repo

| Project | Cơ chế | Mô hình |
|---|---|---|
| **LongRunningTaskApi** (này) | `Channel` + 5 consumer + Polly | Event-driven, xử lý mọi message |
| `BackgroundTaskApi` | `PeriodicTimer` + `SemaphoreSlim` | Định kỳ, *bỏ qua* tick khi đang bận |
| `dotnet-periodic-timer-examples` | `PeriodicTimer` console | Định kỳ, console thuần |
| `system-timers-timer-examples` | `System.Timers.Timer` console | Định kỳ, cách tiếp cận cũ |

Điểm tương phản chính: các project timer **skip** công việc khi bận (phù hợp polling), project này **xếp hàng và đảm bảo xử lý** mọi đơn (phù hợp lệnh/giao dịch).

## Hạn chế / hướng mở rộng

Channel nằm trong bộ nhớ — app crash là mất các đơn đang đợi (và đơn đang xử lý dở lúc shutdown chỉ được log Warning, không tự quay lại hàng đợi); production cần queue bền có cơ chế ack (SQL, RabbitMQ, Azure Service Bus). DLQ hiện là file JSON Lines — đủ cho demo và đã cách ly sau `IDeadLetterQueue`, nhưng chưa có cơ chế replay tự động (mới xem được qua `GET /dead-letters`; xử lý lại đang là việc thủ công). Danh sách lỗi transient (`IsTransient`) đang cố định trong code — nếu nghiệp vụ có thêm loại lỗi tạm thời riêng, ném `TransientOrderException` bọc lỗi gốc.
