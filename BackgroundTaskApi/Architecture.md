# Architecture — BackgroundTaskApi

Minimal API (ASP.NET Core, net10.0) minh họa **tác vụ nền định kỳ có giới hạn** bằng `BackgroundService` + `PeriodicTimer`, với ba điều kiện dừng độc lập và chống chồng chéo tác vụ bằng `SemaphoreSlim`.

## Thành phần

```
Program.cs                 Host: Serilog (console + file), Swagger (Development),
                           đăng ký AddHostedService<MyBackgroundWorker>, endpoint GET /
MyBackgroundWorker.cs      BackgroundService chứa toàn bộ logic vòng lặp định kỳ
appsettings.json           Logging mặc định
```

Gói phụ thuộc: `Serilog.AspNetCore` (log ra console và `logs/BackgroundTaskApi.txt`, rolling theo ngày — thư mục `logs/` bị exclude khỏi build trong .csproj), `Swashbuckle.AspNetCore` (Swagger UI đặt tại route gốc, chỉ bật ở Development).

## Luồng hoạt động của MyBackgroundWorker

```
ExecuteAsync(stoppingToken)
  ├── timeoutCts   = CancellationTokenSource(30s)          ← giới hạn thời gian chạy
  ├── linkedCts    = Linked(stoppingToken, timeoutCts)      ← gộp 2 nguồn hủy
  ├── timer        = PeriodicTimer(1s)
  └── while (i < maxIterations && !linkedCts.IsCancellationRequested)
        ├── await timer.WaitForNextTickAsync(linkedCts.Token)
        ├── semaphore.WaitAsync(0)  ── false → log "bỏ qua lần này"
        │                          └─ true  → DoSomethingHeavy() (3s) → i++ → Release()
        └── OperationCanceledException → log + break
```

## Các quyết định thiết kế

**Ba điều kiện dừng, một vòng lặp.** Worker dừng khi (1) app shutdown (`stoppingToken`), (2) hết 30 giây (`timeoutCts`), hoặc (3) đủ `maxIterations = 10` lần chạy *thành công*. Hai nguồn hủy được gộp bằng `CreateLinkedTokenSource` nên vòng lặp và `WaitForNextTickAsync` chỉ cần quan sát một token duy nhất; điều kiện đếm nằm ngay ở `while`.

**Chống chồng chéo bằng `SemaphoreSlim(1,1)` với `WaitAsync(0)`.** Tác vụ giả lập mất 3 giây nhưng timer tick mỗi 1 giây. `WaitAsync(0)` trả về `false` ngay lập tức nếu tác vụ trước chưa xong — tick đó bị **bỏ qua** (skip) thay vì xếp hàng, nên hệ thống không bao giờ bị dồn ứ tác vụ tồn đọng. Đây là điểm khác biệt chính so với `System.Timers.Timer` (fire-and-forget, dễ chồng chéo).

**Đếm theo lần hoàn thành, không theo tick.** `i++` chỉ chạy sau khi `DoSomethingHeavy()` xong, nên `maxIterations` là "10 lần xử lý thành công", không phải "10 tick".

**`PeriodicTimer` thay vì `Task.Delay` trong vòng lặp.** `WaitForNextTickAsync` giữ nhịp đều theo chu kỳ timer (không bị trôi dần do cộng dồn thời gian xử lý) và hủy được qua `CancellationToken`, cho phép graceful shutdown tức thì.

## So với các project anh em trong repo

| Project | Cơ chế | Bối cảnh |
|---|---|---|
| **BackgroundTaskApi** (này) | `BackgroundService` + `PeriodicTimer` + `SemaphoreSlim` | Web host, vòng lặp có giới hạn lần/thời gian |
| `dotnet-periodic-timer-examples` | `PeriodicTimer` + `SemaphoreSlim` | Console thuần, chạy vô hạn đến Ctrl+C |
| `system-timers-timer-examples` | `System.Timers.Timer` + `SemaphoreSlim` | Console, minh họa cách tiếp cận cũ |
| `LongRunningTaskApi` | `BackgroundService` + `Channel` + Polly | Hàng đợi producer/consumer, không bỏ sót message |

## Hạn chế / hướng mở rộng

Worker chạy xong 10 vòng thì dừng nhưng host web vẫn sống (endpoint `/` vẫn trả lời) — phù hợp demo; service thật muốn dừng cả app có thể inject `IHostApplicationLifetime.StopApplication()`. `maxIterations`, chu kỳ 1s và timeout 30s đang hard-code; đưa vào `appsettings.json` + `IOptions<T>` nếu cần cấu hình.
