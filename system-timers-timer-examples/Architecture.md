# Architecture — system-timers-timer-examples

Console app (net10.0, không package ngoài) minh họa cách tiếp cận **cũ**: `System.Timers.Timer` (event-based) kết hợp `SemaphoreSlim` để chống chồng chéo. Tồn tại trong repo chủ yếu làm **bản đối chứng** cho `dotnet-periodic-timer-examples` (dùng `PeriodicTimer`) — cùng bài toán, hai mô hình.

## Thành phần

Một file `Program.cs`:

```
Main
  ├── _timer = new System.Timers.Timer(1000)   // tick mỗi 1 giây
  ├── _timer.Elapsed += OnTimerElapsed         // event handler
  ├── AutoReset = true; Enabled = true
  ├── Console.ReadKey()                        // block đến khi nhấn phím
  └── Dispose timer + semaphore

OnTimerElapsed (async void!)
  ├── _semaphore.WaitAsync(0) == true  → DoSomethingHeavy() → Release()
  └──                         == false → "Tác vụ trước vẫn đang chạy, bỏ qua lần này."

DoSomethingHeavy — giả lập Task.Delay(3000)
```

## Đặc thù của mô hình event-based (và lý do từng dòng code)

**Timer phải là field `static`.** `System.Timers.Timer` cục bộ có thể bị Garbage Collector thu hồi khi không còn tham chiếu sống — timer im lặng ngừng chạy. Giữ ở field static là cách phòng thủ kinh điển.

**`Elapsed` fire-and-forget → bắt buộc phải tự chống chồng chéo.** Với `AutoReset = true`, timer cứ đến hạn là bắn event trên thread pool, *bất kể* handler trước xong chưa. Tác vụ 3 giây + tick 1 giây nghĩa là không có semaphore sẽ có 3 handler chạy đè nhau. `SemaphoreSlim(1,1).WaitAsync(0)` cho hành vi giống bản `PeriodicTimer`: bận thì bỏ qua tick.

**`async void` là điểm yếu cố hữu.** Event handler buộc phải là `async void` — exception thoát ra khỏi handler sẽ crash process (không catch được từ bên ngoài), không await được, không có backpressure. Code này an toàn vì mọi lỗi nằm trong `Task.Delay`, nhưng đây chính là loại rủi ro mà `PeriodicTimer` loại bỏ bằng thiết kế.

**Không có cancellation.** Dừng bằng `Console.ReadKey()` rồi `Dispose()` — không có `CancellationToken`, nên một tác vụ đang chạy giữa chừng khi nhấn phím sẽ bị bỏ rơi (process thoát khi `Main` return). Bản `PeriodicTimer` xử lý việc này đúng đắn qua token.

## So sánh trực tiếp với `PeriodicTimer` (project anh em)

| | `System.Timers.Timer` (này) | `PeriodicTimer` |
|---|---|---|
| Mô hình | Event `Elapsed`, push | `await WaitForNextTickAsync`, pull |
| Chồng chéo | Tự bắn đè, phải tự chặn | Không thể — một consumer, một vòng lặp |
| Exception | `async void`, có thể crash process | Nổi lên trong vòng lặp, catch bình thường |
| Cancellation | Không có sẵn | Nhận `CancellationToken` trực tiếp |
| GC | Phải giữ tham chiếu static | Biến local trong vòng lặp là đủ |
| Khuyến nghị | Code legacy | .NET 6+ mặc định nên dùng |

## Vị trí trong repo

Cùng bài toán "tác vụ nặng 3s, tick 1s, skip khi bận" được giải bằng: `PeriodicTimer` console (`dotnet-periodic-timer-examples`), `BackgroundService` + `PeriodicTimer` trong web host (`BackgroundTaskApi`), và hàng đợi `Channel` không bỏ sót message (`LongRunningTaskApi`). Project này là mốc so sánh cho thấy vì sao các API mới đáng dùng hơn.
