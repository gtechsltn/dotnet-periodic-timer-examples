# Architecture — LongRunningTaskApi

Minimal API (ASP.NET Core, net10.0) minh họa pipeline xử lý đơn hàng **producer/consumer không bỏ sót message**: API nhận đơn → đẩy vào `Channel` có giới hạn → nhiều consumer song song xử lý với retry (Polly) → graceful shutdown xử lý nốt đơn tồn đọng.

## Thành phần

```
Program.cs               Host: Serilog, Swagger, DI, endpoint POST /order/{orderId},
                         hook ApplicationStopping để đóng Channel
OrderChannel.cs          Bọc Channel<string> bounded (100), expose Writer/Reader
OrderProcessorWorker.cs  BackgroundService: 5 consumer song song + Polly retry
appsettings.json         Logging mặc định
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
Channel<string> bounded(100, FullMode=Wait)  ── đóng khi app dừng
      │
      ▼  Reader.ReadAllAsync(stoppingToken)
OrderProcessorWorker: 5 consumer (Enumerable.Range + Task.WhenAll)
      │  mỗi đơn: _retryPolicy.ExecuteAsync(DoSomethingHeavy)  ← 3 lần, cách 2s
      ▼
  thành công → log "Đã xong đơn"
  thất bại sau retry → log Error (chỗ dành cho Dead Letter Queue)
```

## Các quyết định thiết kế

**Backpressure bằng bounded Channel.** `Channel.CreateBounded(100, FullMode = Wait)`: khi hàng đợi đầy, producer (`WriteAsync` trong endpoint) *đợi* thay vì làm tràn bộ nhớ hoặc drop đơn. Endpoint trả `202 Accepted` — nhận đơn và xử lý được tách rời hoàn toàn.

**`OrderChannel` là singleton thay vì `Channel<T>` trực tiếp.** Lớp bọc cho DI một kiểu có tên rõ ràng, và chỗ duy nhất quyết định dung lượng/FullMode.

**Song song có kiểm soát.** `MaxDegreeOfParallelism = 5` consumer cùng đọc một `ChannelReader` — Channel tự phân phối an toàn giữa các reader, không cần lock. Mỗi consumer là một vòng `await foreach (Reader.ReadAllAsync(...))`; `Task.WhenAll` giữ `ExecuteAsync` sống đến khi tất cả dừng.

**Resilience bằng Polly.** `WaitAndRetryAsync(3, 2s)` cho `Exception`; mỗi lần retry log Warning. Thất bại sau 3 lần được catch ở consumer, log Error và **không** ném tiếp — một đơn hỏng không giết cả worker. Comment trong code đánh dấu đây là chỗ gắn Dead Letter Queue thật.

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

Channel nằm trong bộ nhớ — app crash là mất đơn đang đợi; production cần queue bền (SQL, RabbitMQ, Azure Service Bus). `MaxDegreeOfParallelism`, dung lượng 100 và policy retry đang hard-code; nên đưa vào cấu hình. Retry hiện `Handle<Exception>` (mọi lỗi) với delay cố định — cân nhắc lọc lỗi transient + exponential backoff. Nhánh thất bại mới chỉ log, chưa có Dead Letter Queue thật.
